namespace ExitInterviewAgent.InterviewService.Infrastructure.Auth;

/// <summary>
/// The MCP resource server's configuration, bound from <c>Mcp</c>. All of it is public information (no secret):
/// <list type="bullet">
/// <item><c>Mcp:Issuer</c> must equal authservice's <c>Jwt:PublicBaseUrl</c> exactly (trailing slash ignored).</item>
/// <item><c>Mcp:Resource</c> is this service's canonical MCP URI, the <c>aud</c> of every MCP token.</item>
/// <item><c>Mcp:MetadataAddress</c> is where the RFC 8414 document is read from; defaults to <c>Jwt:Authority</c>.</item>
/// </list>
/// Unset, the MCP scheme still registers but validates nothing, and <c>/health</c> says so.
/// </summary>
public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    public string? Issuer { get; set; }
    public string? Resource { get; set; }
    public string? MetadataAddress { get; set; }

    /// <summary>The normalised issuer, or null when unset or not an absolute URL.</summary>
    public string? NormalizedIssuer => Uri.TryCreate(Issuer?.Trim(), UriKind.Absolute, out var uri)
        && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && IsSecureOrLoopback(uri)
        ? Issuer!.Trim().TrimEnd('/')
        : null;

    /// <summary>
    /// Canonical form of the resource: lowercase scheme and host, no trailing slash, no query or fragment, and a
    /// non-empty path (a token audience must name the MCP endpoint, not the origin).
    /// </summary>
    public string? CanonicalResource => TryCanonicalize(Resource, out var canonical) ? canonical : null;

    /// <summary>The path of the resource, for example <c>/mcp</c>.</summary>
    public string? ResourcePath => CanonicalResource is { } r ? new Uri(r).AbsolutePath : null;

    /// <summary>RFC 9728 well-known URL for the resource: the well-known segment is inserted before the path.</summary>
    public string? ProtectedResourceMetadataUrl => CanonicalResource is { } r
        ? $"{new Uri(r).GetLeftPart(UriPartial.Authority)}/.well-known/oauth-protected-resource{ResourcePath}"
        : null;

    public bool IsConfigured => NormalizedIssuer is not null && CanonicalResource is not null;

    public string Describe() => IsConfigured
        ? $"validating MCP tokens issued by {NormalizedIssuer} for {CanonicalResource}"
        : "Mcp:Issuer and Mcp:Resource not set (or invalid); MCP endpoints answer 401";

    private static bool TryCanonicalize(string? value, out string canonical)
    {
        canonical = string.Empty;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || !IsSecureOrLoopback(uri))
        {
            return false;
        }
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Length == 0)
        {
            return false;
        }
        canonical = $"{uri.GetLeftPart(UriPartial.Authority)}{path}";
        return true;
    }

    private static bool IsSecureOrLoopback(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
}
