using System.Text.RegularExpressions;

namespace ExitInterviewAgent.TestSupport;

/// <summary>
/// The words that must not appear in any record or extractor schema property, nor in a record model member: per-person
/// identifiers, timestamps, and emotion, sentiment or affect (ADR-0011, brief section 6). One list, compiled into every test
/// project that guards a schema, so the record schema and the extractor schema are held to the same rule.
/// </summary>
internal static class ForbiddenFieldNames
{
    public static readonly HashSet<string> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        "user", "username", "account", "acct", "email", "mail", "ip", "ipv4", "ipv6", "address", "phone", "mobile", "msisdn",
        "login", "subject", "sub", "device", "fingerprint", "cookie", "session", "token", "jwt", "name", "firstname", "lastname",
        "surname", "person", "employee", "staff", "worker", "pesel", "nip", "ssn", "passport", "timestamp", "created", "updated",
        "emotion", "emotional", "sentiment", "mood", "affect", "feeling", "tone", "anger", "angry", "stress", "satisfaction", "happiness",
        "submitted", "submission", "ticket", "receipt", "hmac", "hash", "geo", "lat", "lon", "latitude", "longitude",
    };

    public static IEnumerable<string> SplitWords(string name) =>
        Regex.Matches(name, "[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+").Select(m => m.Value);

    /// <summary>Names that contain a forbidden word (or the bare word "id"), except those in <paramref name="allowed"/>.</summary>
    public static IEnumerable<string> Violations(IEnumerable<string> names, ISet<string>? allowed = null) =>
        names.Where(n => allowed?.Contains(n) != true && SplitWords(n).Any(w => Words.Contains(w) || w.Equals("id", StringComparison.OrdinalIgnoreCase)));
}
