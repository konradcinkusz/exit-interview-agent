namespace ExitInterviewAgent.Cli;

/// <summary>
/// The base address of the service that receives submissions. There is no default: a CLI that posts to an address nobody chose would
/// be a CLI that can be pointed at the wrong party by a release. HTTPS is required; plain HTTP is allowed only to a loopback address
/// (a service on this computer, and the tests). The address may not carry credentials, a path, a query or a fragment: those are where
/// secrets leak from. Errors name the rule, never the text the user typed.
/// </summary>
internal sealed record ServerUrl(Uri Base)
{
    public const string EnvironmentVariable = "EXIT_INTERVIEW_SERVER_URL";

    /// <summary>Scheme, host and port only: what is safe to show.</summary>
    public string Display => Base.GetLeftPart(UriPartial.Authority);

    public static ServerUrl Resolve(string? flag, Func<string, string?> env)
    {
        var text = !string.IsNullOrWhiteSpace(flag) ? flag : env(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException($"No server address. Pass --server <url> or set {EnvironmentVariable}. There is deliberately no default: use the address of the deployment you mean to submit to (the web panel's /cli page shows it).");
        return Parse(text.Trim());
    }

    /// <summary>Null when nothing was configured; an address that was given but is not acceptable is still an error.</summary>
    public static ServerUrl? TryResolve(string? flag, Func<string, string?> env) =>
        string.IsNullOrWhiteSpace(flag) && string.IsNullOrWhiteSpace(env(EnvironmentVariable)) ? null : Resolve(flag, env);

    public static ServerUrl Parse(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) throw new ArgumentException("The server address is not a valid absolute URL.");
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) throw new ArgumentException("The server address must start with https://.");
        if (uri.UserInfo.Length > 0) throw new ArgumentException("The server address must not contain a user name or password: a secret in a URL ends up in shell history and logs.");
        if (uri.Query.Length > 0 || uri.Fragment.Length > 0) throw new ArgumentException("The server address must not contain a query string or fragment.");
        if (uri.AbsolutePath != "/") throw new ArgumentException("The server address must be the bare origin (scheme, host, optional port), without a path.");
        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback) throw new ArgumentException("Plain http:// is accepted only for a service on this computer (localhost, 127.0.0.1, ::1). Use https://.");
        return new ServerUrl(new Uri(uri.GetLeftPart(UriPartial.Authority)));
    }
}
