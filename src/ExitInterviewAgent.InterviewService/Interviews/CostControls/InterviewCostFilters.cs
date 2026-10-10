using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.InterviewService.Infrastructure;
using ExitInterviewAgent.InterviewService.Submissions;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Interviews.CostControls;

/// <summary>
/// The cost checks as endpoint filters, the one place they are attached (the routes call these, the rules live in the
/// services above). A refusal uses the kernel's 429 body (<c>{ error, retryAfter }</c>) and the RFC 9457 problem with a
/// stable code for everything else (ADR-0076). Nothing here reads or logs request content.
/// </summary>
public static class InterviewCostFilters
{
    private const string ProblemType = "urn:exit-interview-agent:problem:";

    /// <summary>The stable code of the verified-email refusal (ADR-0078). It is not in the contract yet; the plan's change is proposed in the PR.</summary>
    public const string EmailNotVerified = "email_not_verified";

    /// <summary>
    /// A start, in this order: the per-account and per-address limit, the emergency switch, the verified-email gate, then the
    /// daily cap. Only a 201 is counted as started; any other answer gives the day's slot back.
    /// </summary>
    public static RouteHandlerBuilder WithInterviewStartControls(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            var services = http.RequestServices;
            var metrics = services.GetRequiredService<CostMetrics>();

            if (Admit(http, InterviewRateKind.Start) is { } wait)
            {
                metrics.RateLimited();
                return RateLimited(http, wait);
            }

            if (!services.GetRequiredService<IOptionsMonitor<InterviewServiceOptions>>().CurrentValue.Enabled)
            {
                return Refuse(StatusCodes.Status503ServiceUnavailable, InterviewCodes.InterviewsDisabled);
            }

            if (services.GetRequiredService<IOptions<CostControlOptions>>().Value.RequireVerifiedEmail && !HasVerifiedEmail(http))
            {
                metrics.RejectedEmailUnverified();
                return Refuse(StatusCodes.Status403Forbidden, EmailNotVerified);
            }

            var cap = services.GetRequiredService<DailyStartCap>();
            if (cap.TryReserve(out var retryAfter) is not { } day)
            {
                http.Response.Headers.RetryAfter = retryAfter.ToString();
                return Refuse(StatusCodes.Status503ServiceUnavailable, InterviewCodes.InterviewsDisabled);
            }

            var started = false;
            try
            {
                var result = await next(ctx);
                if (result is IStatusCodeHttpResult { StatusCode: StatusCodes.Status201Created })
                {
                    started = true;
                    metrics.Started();
                }
                return result;
            }
            finally
            {
                if (!started) cap.Release(day);
            }
        });

    /// <summary>A reply: the per-account and per-address limit only. The emergency switch stops new sessions, not a running one.</summary>
    public static RouteHandlerBuilder WithInterviewReplyControls(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            if (Admit(http, InterviewRateKind.Reply) is not { } wait) return await next(ctx);

            http.RequestServices.GetRequiredService<CostMetrics>().RateLimited();
            return RateLimited(http, wait);
        });

    /// <summary>Null when admitted; otherwise the seconds to wait. The account is the subject; the address is resolved once, by <see cref="ClientKey"/>.</summary>
    private static int? Admit(HttpContext http, InterviewRateKind kind)
    {
        var account = http.User.FindFirst("sub")?.Value ?? string.Empty;
        var header = http.RequestServices.GetRequiredService<IOptions<SubmissionOptions>>().Value.ClientIpHeader;
        var address = ClientKey.Resolve(http, header);
        return http.RequestServices.GetRequiredService<InterviewRateLimits>().Admit(kind, account, address);
    }

    /// <summary>
    /// The email-verified flag. It is a boolean the token carries; authservice does not send it today (ADR-0078), so until it
    /// does, this answers false for every account and the gate refuses every start (fail closed).
    /// </summary>
    private static bool HasVerifiedEmail(HttpContext http) =>
        string.Equals(http.User.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase);

    private static IResult RateLimited(HttpContext http, int retryAfter)
    {
        http.Response.Headers.RetryAfter = retryAfter.ToString();
        return Results.Json(new { error = "rate_limited", retryAfter }, statusCode: StatusCodes.Status429TooManyRequests);
    }

    private static IResult Refuse(int status, string code) =>
        Results.Problem(statusCode: status, title: code, type: ProblemType + code, extensions: new Dictionary<string, object?> { ["code"] = code });
}
