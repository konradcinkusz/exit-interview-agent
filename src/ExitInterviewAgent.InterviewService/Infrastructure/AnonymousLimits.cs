using System.Threading.RateLimiting;
using ExitInterviewAgent.InterviewService.Submissions;
using ExitInterviewAgent.ServiceDefaults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Infrastructure;

/// <summary>
/// Limits for the surfaces that have no account: receipt deletion and ticketed submission (ADR-0029, ADR-0030). Each gets a
/// strict per-client window and a global budget stacked beneath it, because a window per client does not stop every client
/// together. Rejections are immediate (no queue) and use the kernel's 429 body shape. The client key is resolved in one
/// place (<see cref="ClientKey"/>) and is used only as a limiter partition: it is never stored or logged.
/// </summary>
public static class AnonymousLimits
{
    public const string ReceiptDelete = "receipt-delete";
    public const string TicketedSubmit = "ticketed-submit";
    public const string TicketMint = "ticket-mint";

    public static IServiceCollection AddSubmissionRateLimits(this IServiceCollection services)
    {
        services.AddSingleton<GlobalBudgets>();
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<SubmissionOptions>>((o, submission) =>
        {
            var limits = submission.Value.Limits;
            var header = submission.Value.ClientIpHeader;
            o.AddPolicy(ReceiptDelete, c => Window(ClientKey.Resolve(c, header), limits.ReceiptDeletePerIpPerMinute, TimeSpan.FromMinutes(1)));
            o.AddPolicy(TicketedSubmit, c => Window(ClientKey.Resolve(c, header), limits.TicketedSubmitPerIpPerMinute, TimeSpan.FromMinutes(1)));
            // Per account, not per address: minting is authenticated, and the account is what is being limited.
            o.AddPolicy(TicketMint, c => Window(c.User.FindFirst("sub")?.Value ?? ClientKey.Resolve(c, header), submission.Value.Tickets.MintsPerAccountPerHour, TimeSpan.FromHours(1)));
        });
        return services;
    }

    private static RateLimitPartition<string> Window(string key, int permits, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = window, QueueLimit = 0 });

    /// <summary>The global budget of one anonymous endpoint, as an endpoint filter.</summary>
    public static RouteHandlerBuilder WithGlobalBudget(this RouteHandlerBuilder builder, string name) =>
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var budgets = ctx.HttpContext.RequestServices.GetRequiredService<GlobalBudgets>();
            if (!budgets.TryAcquire(name))
            {
                ctx.HttpContext.Response.Headers.RetryAfter = "60";
                return Results.Json(new { error = "rate_limited", retryAfter = 60 }, statusCode: StatusCodes.Status429TooManyRequests);
            }
            return await next(ctx);
        });
}

/// <summary>Process-wide fixed-window budgets, one per anonymous endpoint.</summary>
public sealed class GlobalBudgets(IOptions<SubmissionOptions> options) : IDisposable
{
    private readonly Dictionary<string, FixedWindowRateLimiter> _limiters = new()
    {
        [AnonymousLimits.ReceiptDelete] = Make(options.Value.Limits.ReceiptDeleteGlobalPerMinute),
        [AnonymousLimits.TicketedSubmit] = Make(options.Value.Limits.TicketedSubmitGlobalPerMinute),
    };

    private static FixedWindowRateLimiter Make(int permits) =>
        new(new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

    public bool TryAcquire(string name)
    {
        using var lease = _limiters[name].AttemptAcquire();
        return lease.IsAcquired;
    }

    public void Dispose()
    {
        foreach (var limiter in _limiters.Values)
        {
            limiter.Dispose();
        }
    }
}

/// <summary>
/// The one place the client is resolved. The forwarded header is read ONLY when configuration names one (a proxy is in
/// front); otherwise it is ignored, because with nothing in front it is client-supplied.
/// </summary>
public static class ClientKey
{
    public static string Resolve(HttpContext context, string? trustedHeader)
    {
        if (!string.IsNullOrWhiteSpace(trustedHeader) && context.Request.Headers.TryGetValue(trustedHeader, out var values)
            && values.ToString().Split(',')[^1].Trim() is { Length: > 0 and <= 64 } forwarded)
        {
            return forwarded;
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
