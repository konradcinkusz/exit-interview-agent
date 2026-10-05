using System.Reflection;
using System.Text.Json;

namespace ExitInterviewAgent.Records;

/// <summary>The embedded, immutable v1 schema (published at schemas/exit-interview-record.v1.schema.json).</summary>
public static class RecordSchema
{
    public const string Id = "urn:exit-interview-agent:schema:exit-interview-record:v1";
    private const string Resource = "exit-interview-record.v1.schema.json";

    public static string JsonText { get; } = Load();

    /// <summary>Every property name the schema defines; the only names an error path may contain.</summary>
    internal static IReadOnlySet<string> KnownNames { get; } = CollectNames(JsonText);

    private static string Load()
    {
        using var stream = typeof(RecordSchema).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"Embedded schema resource '{Resource}' is missing.");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static HashSet<string> CollectNames(string schema)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(schema);
        Walk(doc.RootElement);
        return names;

        void Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Array) { foreach (var i in e.EnumerateArray()) Walk(i); return; }
            if (e.ValueKind != JsonValueKind.Object) return;
            foreach (var p in e.EnumerateObject())
            {
                if (p.Name == "properties" && p.Value.ValueKind == JsonValueKind.Object)
                    foreach (var prop in p.Value.EnumerateObject()) names.Add(prop.Name);
                Walk(p.Value);
            }
        }
    }
}
