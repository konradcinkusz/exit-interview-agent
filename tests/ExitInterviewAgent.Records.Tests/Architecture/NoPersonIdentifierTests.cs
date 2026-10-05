using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ExitInterviewAgent.Records.Tests.Architecture;

/// <summary>
/// The record must not carry, and its schema must not be able to carry, anything that identifies a person or
/// links records to one: user id, account id, email, IP, name, device, timestamp (ADR-0011, brief section 6).
/// An anti-goal enforced by architecture: adding such a field fails this build.
/// </summary>
public class NoPersonIdentifierTests
{
    private static readonly HashSet<string> ForbiddenWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "user", "username", "account", "acct", "email", "mail", "ip", "ipv4", "ipv6", "address", "phone", "mobile", "msisdn",
        "login", "subject", "sub", "device", "fingerprint", "cookie", "session", "token", "jwt", "name", "firstname", "lastname",
        "surname", "person", "employee", "staff", "worker", "pesel", "nip", "ssn", "passport", "timestamp", "created", "updated",
        "submitted", "submission", "ticket", "receipt", "hmac", "hash", "geo", "lat", "lon", "latitude", "longitude",
    };

    /// <summary>The only identifier-shaped member allowed: the random, pseudonymous interview id.</summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) { "interviewId", "InterviewId" };

    internal static IEnumerable<string> Violations(IEnumerable<string> names) =>
        names.Where(n => !Allowed.Contains(n) && Words(n).Any(w => ForbiddenWords.Contains(w) || w.Equals("id", StringComparison.OrdinalIgnoreCase)));

    private static IEnumerable<string> Words(string name) =>
        Regex.Matches(name, "[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+").Select(m => m.Value);

    [Fact]
    public void The_guard_can_fail_it_flags_identifier_shaped_names()
    {
        var bad = new[] { "userId", "user_id", "accountId", "email", "ipAddress", "clientIp", "sub", "createdAt", "submittedAt", "employeeId", "displayName", "deviceId", "ssn", "pesel", "id", "sessionId" };

        Assert.Equal(bad.Order(), Violations(bad).Order());
    }

    [Fact]
    public void The_guard_leaves_the_legitimate_names_alone()
    {
        var fine = new[] { "interviewId", "employerRef", "tenureBand", "seniorityBand", "functionBand", "topics", "pay_vs_promises", "reason_for_leaving",
                           "rating", "confidence", "quotes", "status", "piiMasked", "protocolVersion", "language", "durationBand", "turnBand", "schemaVersion" };

        Assert.Empty(Violations(fine));
    }

    [Fact]
    public void No_schema_property_name_resembles_a_per_person_identifier()
    {
        var names = SchemaPropertyNames();

        Assert.NotEmpty(names);
        Assert.Empty(Violations(names));
    }

    [Fact]
    public void Every_object_in_the_schema_is_closed_so_no_unlisted_field_can_ride_along()
    {
        var open = new List<string>();
        using var doc = JsonDocument.Parse(RecordSchema.JsonText);
        Walk(doc.RootElement, "#");
        Assert.Empty(open);

        void Walk(JsonElement e, string path)
        {
            if (e.ValueKind == JsonValueKind.Array) { var i = 0; foreach (var x in e.EnumerateArray()) Walk(x, $"{path}/{i++}"); return; }
            if (e.ValueKind != JsonValueKind.Object) return;
            // if/then/else fragments only constrain properties the closed parent object already declares.
            if (path.EndsWith("/if", StringComparison.Ordinal) || path.EndsWith("/then", StringComparison.Ordinal) || path.EndsWith("/else", StringComparison.Ordinal)) return;
            var isObjectSchema = e.TryGetProperty("properties", out _) ||
                (e.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() == "object");
            if (isObjectSchema && !(e.TryGetProperty("additionalProperties", out var ap) && ap.ValueKind == JsonValueKind.False)) open.Add(path);
            foreach (var p in e.EnumerateObject()) Walk(p.Value, $"{path}/{p.Name}");
        }
    }

    [Fact]
    public void No_context_field_is_free_text()
    {
        using var doc = JsonDocument.Parse(RecordSchema.JsonText);
        var context = doc.RootElement.GetProperty("properties").GetProperty("context").GetProperty("properties");
        foreach (var field in context.EnumerateObject())
            Assert.True(field.Value.TryGetProperty("enum", out _), $"context.{field.Name} must be an enum of coarse bands.");
    }

    [Fact]
    public void The_model_exposes_no_per_person_identifier()
    {
        var types = typeof(InterviewRecord).Assembly.GetExportedTypes()
            .Where(t => t.Namespace == "ExitInterviewAgent.Records" && (t.IsClass || t.IsValueType) && !t.IsAbstract && t.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is null)
            .Where(t => t.Name is "InterviewRecord" or "RecordContext" or "InterviewMetadata" or "TopicEntry" or "TopicSet")
            .ToArray();
        Assert.Equal(5, types.Length);

        var names = types.SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0).Select(p => p.Name)
            .Concat(t.GetConstructors().SelectMany(c => c.GetParameters().Select(p => p.Name!))));

        Assert.Empty(Violations(names.Distinct()));
    }

    [Fact]
    public void Serialized_golden_records_contain_only_schema_names()
    {
        var schemaNames = SchemaPropertyNames().ToHashSet();
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid"), "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var key in KeysOutsideQuotes(doc.RootElement)) Assert.Contains(key, schemaNames);
        }
    }

    private static IEnumerable<string> KeysOutsideQuotes(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) yield break;
        foreach (var p in e.EnumerateObject())
        {
            yield return p.Name;
            foreach (var k in KeysOutsideQuotes(p.Value)) yield return k;
        }
    }

    private static List<string> SchemaPropertyNames()
    {
        var names = new List<string>();
        using var doc = JsonDocument.Parse(RecordSchema.JsonText);
        Walk(doc.RootElement);
        return names;

        void Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Array) { foreach (var x in e.EnumerateArray()) Walk(x); return; }
            if (e.ValueKind != JsonValueKind.Object) return;
            foreach (var p in e.EnumerateObject())
            {
                if (p.Name == "properties" && p.Value.ValueKind == JsonValueKind.Object) names.AddRange(p.Value.EnumerateObject().Select(x => x.Name));
                Walk(p.Value);
            }
        }
    }
}
