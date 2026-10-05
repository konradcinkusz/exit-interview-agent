using System.Text.Json;
using ExitInterviewAgent.InterviewService.Endpoints;
using ExitInterviewAgent.InterviewService.Infrastructure.Auth;
using ExitInterviewAgent.Records;
using Microsoft.AspNetCore.Http.Features;

namespace ExitInterviewAgent.InterviewService.Mcp;

/// <summary>
/// The transport-level protections the MCP specification asks of an HTTP server, applied to the MCP path before
/// authentication and before the SDK sees the request:
/// <list type="bullet">
/// <item><b>Origin validation</b> (DNS-rebinding and cross-site protection): a request that carries an <c>Origin</c> header
/// that is not on <c>Mcp:AllowedOrigins</c> is refused with 403. Non-browser clients (Claude's connector) send none.</item>
/// <item><b>Protocol version</b>: an <c>MCP-Protocol-Version</c> header that names a revision this server does not speak is
/// refused with 400; an absent header is allowed (the specification says to assume 2025-03-26).</item>
/// <item><b>Closed vocabulary</b>: method, tool, prompt and resource names outside this server's fixed set are replaced before the
/// SDK reads the body (<see cref="McpBodyScrubber"/>), so client-chosen text never reaches the SDK's logs, spans or metrics.</item>
/// <item><b>Size</b>: a request body over <see cref="MaxRequestBytes"/> is refused with 413 on its declared length and capped
/// on the server's own limit while it is read, so an oversized record never reaches the record library.</item>
/// </list>
/// Refusals are problem details with a fixed code and never echo the offending header value.
/// </summary>
public static class McpTransportGuard
{
    /// <summary>Largest record plus room for the JSON-RPC envelope.</summary>
    public static readonly int MaxRequestBytes = RecordLimits.Default.MaxPayloadBytes + 16 * 1024;

    /// <summary>Revisions this server accepts in the header. A test pins each against the SDK's own list.</summary>
    public static readonly IReadOnlyList<string> SupportedProtocolVersions = ["2024-11-05", "2025-03-26", "2025-06-18", "2025-11-25", "2026-07-28"];

    public const string ProtocolVersionHeader = "MCP-Protocol-Version";

    public static WebApplication UseMcpTransportGuard(this WebApplication app)
    {
        var mcp = app.Services.GetRequiredService<McpOptions>();
        if (mcp.ResourcePath is not { } path)
        {
            return app;
        }
        var allowed = new HashSet<string>(mcp.AllowedOrigins.Select(NormalizeOrigin).Where(o => o is not null)!, StringComparer.Ordinal);
        app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase), branch => branch.Use(async (ctx, next) =>
        {
            var refusal = Check(ctx.Request, allowed);
            if (refusal is { } r)
            {
                ctx.Response.StatusCode = r.Status;
                ctx.Response.ContentType = "application/problem+json";
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { type = "urn:exit-interview-agent:problem:" + r.Code.ToLowerInvariant(), title = r.Code, status = r.Status, code = r.Code }));
                return;
            }
            if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
            {
                feature.MaxRequestBodySize = MaxRequestBytes;
            }
            if (HttpMethods.IsPost(ctx.Request.Method))
            {
                // Bounded read (never more than the limit plus one byte), then the closed vocabulary (McpBodyScrubber).
                var body = await BoundedBody.ReadAsync(ctx.Request, MaxRequestBytes, ctx.RequestAborted);
                if (body is null)
                {
                    ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    ctx.Response.ContentType = "application/problem+json";
                    await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { type = "urn:exit-interview-agent:problem:payload_too_large", title = "PAYLOAD_TOO_LARGE", status = 413, code = "PAYLOAD_TOO_LARGE" }));
                    return;
                }
                var scrubbed = McpBodyScrubber.Scrub(body);
                ctx.Request.Body = new MemoryStream(scrubbed, writable: false);
                ctx.Request.ContentLength = scrubbed.Length;
            }
            await next();
        }));
        return app;
    }

    /// <summary>The decision, separated from the middleware so it can be tested on its own.</summary>
    public static (int Status, string Code)? Check(HttpRequest request, IReadOnlySet<string> allowedOrigins)
    {
        if (request.Headers.TryGetValue("Origin", out var origin) && origin.Count > 0)
        {
            if (NormalizeOrigin(origin.ToString()) is not { } normalized || !allowedOrigins.Contains(normalized))
            {
                return (StatusCodes.Status403Forbidden, "ORIGIN_NOT_ALLOWED");
            }
        }
        if (request.Headers.TryGetValue(ProtocolVersionHeader, out var version) && version.Count > 0
            && !SupportedProtocolVersions.Contains(version.ToString(), StringComparer.Ordinal))
        {
            return (StatusCodes.Status400BadRequest, "UNSUPPORTED_PROTOCOL_VERSION");
        }
        if (request.ContentLength > MaxRequestBytes)
        {
            return (StatusCodes.Status413PayloadTooLarge, "PAYLOAD_TOO_LARGE");
        }
        return null;
    }

    /// <summary>scheme://host[:port], lower case, no path. "null" and anything that is not an absolute http(s) origin are not origins.</summary>
    internal static string? NormalizeOrigin(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && (uri.AbsolutePath is "/" or "")
        && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.UserInfo)
            ? uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant()
            : null;
}
