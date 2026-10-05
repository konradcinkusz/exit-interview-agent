using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace ExitInterviewAgent.ServiceDefaults;

/// <summary>Rate limiting, validation, list clamping and the OpenAPI document (SERVICE-API-PATTERNS §1, §3, §4).</summary>
public static class ApiExtensions
{
    public const string ApiPolicy = "api";
    public const string AuthPolicy = "auth";

    /// <summary>
    /// Fixed window per authenticated subject, falling back to the remote address; uniform 429 body; rejections are
    /// not queued. Behind a platform proxy the remote address is the proxy until forwarded headers are configured
    /// as trusted (deployment concern, documented in docs/architecture).
    /// </summary>
    public static IServiceCollection AddStandardRateLimiting(this IServiceCollection services, int apiPerMinute = 120, int authPerMinute = 10)
    {
        static string Key(HttpContext c) => c.User.FindFirst("sub")?.Value ?? c.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        static FixedWindowRateLimiterOptions Window(int permits) => new()
        {
            PermitLimit = permits,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        };
        return services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(ApiPolicy, c => RateLimitPartition.GetFixedWindowLimiter(Key(c), _ => Window(apiPerMinute)));
            o.AddPolicy(AuthPolicy, c => RateLimitPartition.GetFixedWindowLimiter(Key(c), _ => Window(authPerMinute)));
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c =>
                c.Request.Path.StartsWithSegments("/health") || c.Request.Path.StartsWithSegments("/alive")
                    ? RateLimitPartition.GetNoLimiter("probes")
                    : RateLimitPartition.GetFixedWindowLimiter(Key(c), _ => Window(apiPerMinute * 2)));
            o.OnRejected = async (ctx, ct) =>
            {
                var retryAfter = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ra) ? (int)ra.TotalSeconds : 60;
                ctx.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();
                await ctx.HttpContext.Response.WriteAsJsonAsync(new { error = "rate_limited", retryAfter }, ct);
            };
        });
    }

    /// <summary>Runs DataAnnotations on every bound argument; one uniform <c>ValidationProblem</c> shape.</summary>
    public static RouteGroupBuilder WithValidation(this RouteGroupBuilder group)
        => group.AddEndpointFilter(async (ctx, next) =>
        {
            var errors = new Dictionary<string, string[]>();
            foreach (var arg in ctx.Arguments.Where(a => a is not null && !a.GetType().IsPrimitive && a is not string))
            {
                var results = new List<ValidationResult>();
                if (Validator.TryValidateObject(arg!, new ValidationContext(arg!), results, validateAllProperties: true))
                {
                    continue;
                }
                foreach (var g in results.GroupBy(r => r.MemberNames.FirstOrDefault() ?? string.Empty))
                {
                    errors[g.Key] = [.. g.Select(r => r.ErrorMessage ?? "invalid")];
                }
            }
            return errors.Count > 0 ? Results.ValidationProblem(errors) : await next(ctx);
        });

    /// <summary>Every list endpoint clamps its inputs: <c>page &gt;= 1</c>, <c>1 &lt;= limit &lt;= 100</c> (a DoS control).</summary>
    public static (int Page, int Limit) ClampPage(int? page, int? limit) => (Math.Max(1, page ?? 1), Math.Clamp(limit ?? 25, 1, 100));

    public static IServiceCollection AddOpenApiDocument(this IServiceCollection services, string title, string version, string description)
        => services.AddOpenApi(o => o.AddDocumentTransformer((doc, _, _) =>
        {
            doc.Info = new OpenApiInfo { Title = title, Version = version, Description = description };
            return Task.CompletedTask;
        }));
}
