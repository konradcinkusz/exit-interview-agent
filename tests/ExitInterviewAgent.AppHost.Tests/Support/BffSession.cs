using System.Net;

namespace ExitInterviewAgent.AppHost.Tests.Support;

/// <summary>
/// The browser's side of one signed-in account: the HttpOnly cookies the BFF sets (ADR-0013). The test keeps them the way a
/// browser would, so the BFF is exercised exactly as a page calls it: cookies, and an Origin header on state changes.
/// </summary>
public sealed class BffSession
{
    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

    public string? Get(string name) => _cookies.TryGetValue(name, out var value) ? value : null;

    public string CookieHeader => string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}"));

    /// <summary>Stores every cookie the response sets. An empty value or Max-Age=0 removes the cookie, as a browser does.</summary>
    public void Absorb(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var headers)) return;
        foreach (var header in headers)
        {
            var parsed = ParseSetCookie(header);
            if (parsed is not { } cookie) continue;
            if (cookie.Value.Length == 0 || cookie.Expired) _cookies.Remove(cookie.Name);
            else _cookies[cookie.Name] = cookie.Value;
        }
    }

    /// <summary>One Set-Cookie header: name, value, and whether it expires the cookie at once. Null when it has no name=value pair.</summary>
    public static SetCookie? ParseSetCookie(string header)
    {
        var parts = header.Split(';');
        var pair = parts[0].Trim();
        var eq = pair.IndexOf('=', StringComparison.Ordinal);
        if (eq <= 0) return null;
        var expired = parts.Skip(1).Any(p => p.Trim().Equals("Max-Age=0", StringComparison.OrdinalIgnoreCase));
        return new SetCookie(pair[..eq], pair[(eq + 1)..], expired);
    }

    public sealed record SetCookie(string Name, string Value, bool Expired);

    /// <summary>The access-token cookie, for the calls the test makes straight to a service (the dev grant).</summary>
    public string? AccessToken => Get(Names.Access);

    /// <summary>The cookie names of session.ts. Named here once, so a rename breaks one place and the test says so.</summary>
    public static class Names
    {
        public const string Access = "eia_access";
        public const string Refresh = "eia_refresh";
        public const string Consent = "eia_consent";
    }
}

public static class HttpResponseExtensions
{
    /// <summary>The body as text, for a message that says what came back when a step fails.</summary>
    public static async Task<string> BodyAsync(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return body.Length > 600 ? body[..600] + "..." : body;
    }

    public static async Task EnsureStatusAsync(this HttpResponseMessage response, HttpStatusCode expected, string step)
    {
        if (response.StatusCode == expected) return;
        throw new InvalidOperationException($"{step}: expected {(int)expected}, got {(int)response.StatusCode}. Body: {await response.BodyAsync()}");
    }
}
