using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using LateApexEarlySpeed.Json.Schema;
using LateApexEarlySpeed.Json.Schema.Common;

namespace ExitInterviewAgent.Personas;

/// <summary>The personas that ship in the box, loaded and schema-validated once. A persona that fails validation throws at load, so a bad data file fails every test.</summary>
public static class PersonaCatalog
{
    public static string SchemaText { get; } = ReadResource("persona.v1.schema.json");

    private static readonly Lazy<IReadOnlyList<PersonaDefinition>> Loaded = new(LoadAll);

    /// <summary>All personas, ordered by id.</summary>
    public static IReadOnlyList<PersonaDefinition> All => Loaded.Value;

    public static PersonaDefinition Get(string id) =>
        TryGet(id, out var p) ? p! : throw new KeyNotFoundException($"No persona with id '{id}'. Known: {string.Join(", ", All.Select(x => x.Id))}.");

    public static bool TryGet(string id, out PersonaDefinition? persona)
    {
        persona = All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
        return persona is not null;
    }

    /// <summary>Validates persona JSON against the schema and maps it. Throws <see cref="InvalidDataException"/> with the failing schema location only.</summary>
    public static PersonaDefinition Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowDuplicateProperties = false, MaxDepth = 16 });
        var result = new JsonValidator(SchemaText).Validate(doc.RootElement, new JsonSchemaOptions { OutputFormat = OutputFormat.List, GenerateErrorMessages = false, ValidateFormat = false });
        if (!result.IsValid) throw new InvalidDataException("Persona does not conform to persona.v1.schema.json.");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        return JsonSerializer.Deserialize<PersonaDefinition>(json, options) ?? throw new InvalidDataException("Empty persona.");
    }

    private static IReadOnlyList<PersonaDefinition> LoadAll()
    {
        var assembly = typeof(PersonaCatalog).Assembly;
        var personas = assembly.GetManifestResourceNames().Where(n => n.StartsWith("personas/", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)).Order(StringComparer.Ordinal)
            .Select(n => Parse(ReadResource(n))).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        if (personas.Select(p => p.Id).Distinct().Count() != personas.Count) throw new InvalidDataException("Persona ids must be unique.");
        return personas;
    }

    private static string ReadResource(string name)
    {
        using var s = typeof(PersonaCatalog).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Missing embedded resource '{name}'.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
