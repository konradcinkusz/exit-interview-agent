using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using LateApexEarlySpeed.Json.Schema;
using LateApexEarlySpeed.Json.Schema.Common;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ExitInterviewAgent.Eval.Scenarios;

/// <summary>
/// Reads scenario YAML, converts it to JSON by YAML's core-schema rules (plain scalars become numbers, booleans or null; quoted ones stay
/// strings), validates it against the strict JSON Schema, and only then maps it to the typed model. A mistyped key fails at load.
/// </summary>
public static class ScenarioLoader
{
    private static readonly JsonSerializerOptions Typed = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public static IReadOnlyList<LoadedScenario> LoadAll(string? dir = null)
    {
        dir ??= RepoLayout.ScenariosDir;
        var schema = File.ReadAllText(RepoLayout.SchemaPath);
        return Directory.EnumerateFiles(dir, "*.yaml", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => LoadFile(p, schema))
            .ToList();
    }

    public static LoadedScenario LoadFile(string path, string? schemaText = null)
    {
        var json = YamlToJson(File.ReadAllText(path), path);
        var schema = schemaText ?? File.ReadAllText(RepoLayout.SchemaPath);
        using var doc = JsonDocument.Parse(json);
        var result = new JsonValidator(schema).Validate(doc.RootElement, new JsonSchemaOptions { OutputFormat = OutputFormat.List, GenerateErrorMessages = false, ValidateFormat = false });
        if (!result.IsValid)
        {
            var first = result.ValidationErrors?.FirstOrDefault();
            throw new InvalidDataException($"{Path.GetFileName(path)} does not satisfy scenario.schema.json at '{first?.InstanceLocation?.ToString() ?? "/"}' (keyword '{first?.Keyword ?? "?"}').");
        }
        var scenario = JsonSerializer.Deserialize<Scenario>(json, Typed) ?? throw new InvalidDataException($"{Path.GetFileName(path)} is empty.");
        return new LoadedScenario(scenario, path, Canonical(json));
    }

    public static string YamlToJson(string yaml, string source = "yaml")
    {
        var stream = new YamlStream();
        try { stream.Load(new StringReader(yaml)); }
        catch (YamlException ex) { throw new InvalidDataException($"{Path.GetFileName(source)} is not valid YAML at line {ex.Start.Line}."); }
        if (stream.Documents.Count != 1) throw new InvalidDataException($"{Path.GetFileName(source)} must hold exactly one YAML document.");
        return Convert(stream.Documents[0].RootNode)?.ToJsonString() ?? "null";
    }

    /// <summary>The same JSON with object keys sorted, so the corpus digest does not move when somebody reorders keys.</summary>
    public static string Canonical(string json) => Sort(JsonNode.Parse(json))?.ToJsonString() ?? "null";

    private static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sort(p.Value?.DeepClone())))),
        JsonArray a => new JsonArray(a.Select(n => Sort(n?.DeepClone())).ToArray()),
        _ => node?.DeepClone(),
    };

    private static JsonNode? Convert(YamlNode node) => node switch
    {
        YamlMappingNode m => new JsonObject(m.Children.Select(kv => KeyValuePair.Create(((YamlScalarNode)kv.Key).Value ?? "", Convert(kv.Value)))),
        YamlSequenceNode s => new JsonArray(s.Children.Select(Convert).ToArray()),
        YamlScalarNode sc => Scalar(sc),
        _ => throw new InvalidDataException("Unsupported YAML node (aliases and tags are not allowed in scenarios)."),
    };

    private static JsonNode? Scalar(YamlScalarNode s)
    {
        var v = s.Value;
        if (s.Style != ScalarStyle.Plain) return JsonValue.Create(v ?? "");
        if (v is null or "" or "~" or "null") return null;
        if (v is "true") return JsonValue.Create(true);
        if (v is "false") return JsonValue.Create(false);
        if (long.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)) return JsonValue.Create(l);
        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && !v.StartsWith('.')) return JsonValue.Create(d);
        return JsonValue.Create(v);
    }
}
