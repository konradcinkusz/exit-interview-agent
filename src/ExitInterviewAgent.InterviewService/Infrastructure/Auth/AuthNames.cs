using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace ExitInterviewAgent.InterviewService.Infrastructure.Auth;

/// <summary>The two authentication schemes (ADR-0012). Other code refers to these names, never to string literals.</summary>
public static class AuthSchemes
{
    /// <summary>Web/BFF tokens: authservice's own access tokens (issuer/audience from <c>Jwt:*</c>). The default scheme.</summary>
    public const string Web = JwtBearerDefaults.AuthenticationScheme;

    /// <summary>MCP tokens: issued by authservice's authorization server, audience-bound to this service's MCP resource.</summary>
    public const string Mcp = "McpBearer";
}

/// <summary>Authorization policies. Endpoints pick a policy; they never pick a scheme directly.</summary>
public static class AuthPolicies
{
    /// <summary>A signed-in account through the web/BFF scheme. Not usable from an MCP token.</summary>
    public const string Account = "account";

    /// <summary>An MCP client acting for an account, holding <see cref="McpScopes.InterviewSubmit"/>. Not usable from a web token.</summary>
    public const string McpSubmit = "mcp-submit";
}

/// <summary>Scopes this resource server defines and enforces itself; authservice only carries them.</summary>
public static class McpScopes
{
    public const string InterviewSubmit = "interview:submit";

    /// <summary>
    /// Requested so the client is issued a refresh token. authservice requires it on every client; this service
    /// neither needs nor enforces it, it only advertises it (ADR-0012).
    /// </summary>
    public const string OfflineAccess = "offline_access";

    public static readonly IReadOnlyList<string> Supported = [InterviewSubmit, OfflineAccess];
}
