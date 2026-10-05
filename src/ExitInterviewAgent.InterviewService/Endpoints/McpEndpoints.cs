using ExitInterviewAgent.InterviewService.Infrastructure.Auth;
using ExitInterviewAgent.ServiceDefaults;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExitInterviewAgent.InterviewService.Endpoints;

public static class McpEndpoints
{
    public const string WellKnownPrefix = "/.well-known/oauth-protected-resource";

    /// <summary>
    /// RFC 9728 protected-resource metadata, public. authservice is FIRST in <c>authorization_servers</c>: MCP clients
    /// use the first entry and do not fall back. Served at the path-suffixed URL for the resource and at the bare
    /// well-known URL (clients try both). Unconfigured, it answers 404 (P8).
    /// </summary>
    public static IEndpointRouteBuilder MapProtectedResourceMetadata(this IEndpointRouteBuilder app, McpOptions mcp)
    {
        IResult Metadata(HttpContext http)
        {
            if (!mcp.IsConfigured)
            {
                return Results.NotFound();
            }
            http.Response.Headers.CacheControl = "public, max-age=300";
            return Results.Json(new
            {
                resource = mcp.CanonicalResource,
                authorization_servers = new[] { mcp.NormalizedIssuer },
                scopes_supported = McpScopes.Supported,
                bearer_methods_supported = new[] { "header" },
                resource_name = "Exit Interview Agent",
            }, statusCode: StatusCodes.Status200OK);
        }

        app.MapGet(WellKnownPrefix, Metadata).AllowAnonymous().DisableRateLimiting();
        if (mcp.ResourcePath is { } path)
        {
            app.MapGet($"{WellKnownPrefix}{path}", Metadata).AllowAnonymous().DisableRateLimiting();
        }
        return app;
    }

    /// <summary>
    /// The authenticated, scope-guarded mount point for the MCP endpoint at the resource's path, with the MCP Streamable
    /// HTTP transport mapped on it. Nothing else may be mapped under this group without the policy.
    /// </summary>
    public static RouteGroupBuilder? MapMcpMount(this WebApplication app, McpOptions mcp)
    {
        if (mcp.ResourcePath is not { } path)
        {
            return null;
        }
        var group = app.MapGroup(path).RequireAuthorization(AuthPolicies.McpSubmit).RequireRateLimiting(ApiExtensions.ApiPolicy);
        group.MapMcp();
        return group;
    }
}
