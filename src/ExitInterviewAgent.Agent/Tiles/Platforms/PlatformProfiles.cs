using System.Text.Json;
using System.Text.Json.Serialization;
using LateApexEarlySpeed.Json.Schema;
using LateApexEarlySpeed.Json.Schema.Common;

namespace ExitInterviewAgent.Agent.Tiles.Platforms;

// The loader for platform-profiles.v1.json (docs/architecture/platform-drafts-spec.md §3.1, ADR-0083). The file is embedded,
// checked against schemas/platform-profiles.v1.schema.json and mapped to PlatformProfile records. A file that does not load
// throws InvalidDataException, so the first use fails loudly. The invariants of spec §3.2 that a schema cannot state (the risk
// rule, the URL host allowlist, the family limits, the Google retirement) are tests, not checks here.

/// <summary>The platform profiles the program knows, loaded and validated once (spec §3.1).</summary>
public sealed class PlatformProfiles : IPlatformProfiles
{
    private const string ProfilesResource = "platform-profiles.v1.json";
    private const string SchemaResource = "platform-profiles.v1.schema.json";

    /// <summary>The profile file exactly as embedded. Tests read it for the text-level invariants.</summary>
    internal static string Text { get; } = ReadResource(ProfilesResource);

    private static string SchemaText { get; } = ReadResource(SchemaResource);

    private static readonly JsonValidator Validator = new(SchemaText);

    private static readonly Lazy<PlatformProfiles> Loaded = new(() => Parse(Text));

    private readonly IReadOnlyList<PlatformProfile> _all;
    private readonly IReadOnlyList<PlatformProfile> _enabled;

    private PlatformProfiles(IReadOnlyList<PlatformProfile> all)
    {
        _all = all;
        _enabled = all.Where(p => p.Enabled).ToList();
    }

    /// <summary>The shipped profiles. The first access throws <see cref="InvalidDataException"/> if the file does not load.</summary>
    public static PlatformProfiles Default => Loaded.Value;

    /// <summary>Every profile in file order, enabled or not.</summary>
    public IReadOnlyList<PlatformProfile> All => _all;

    /// <inheritdoc />
    public IReadOnlyList<PlatformProfile> Enabled => _enabled;

    /// <inheritdoc />
    public PlatformProfile Get(string id) =>
        _all.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"No platform profile with id '{id}'.");

    /// <summary>
    /// Validates the JSON against the schema, maps it and checks that the ids are unique. Throws <see cref="InvalidDataException"/>
    /// with a message that names no content: only the schema location or the rule is said.
    /// </summary>
    public static PlatformProfiles Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowDuplicateProperties = false, MaxDepth = 16 }); }
        catch (JsonException) { throw new InvalidDataException("Platform profiles are not well-formed JSON."); }

        using (doc)
        {
            var result = Validator.Validate(doc.RootElement, new JsonSchemaOptions { OutputFormat = OutputFormat.List, GenerateErrorMessages = false, ValidateFormat = false });
            if (!result.IsValid) throw new InvalidDataException("Platform profiles do not conform to platform-profiles.v1.schema.json.");
        }

        var file = JsonSerializer.Deserialize<ProfilesFile>(json, Options) ?? throw new InvalidDataException("Empty platform profiles.");
        var profiles = file.Profiles;
        if (profiles.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != profiles.Count)
        {
            throw new InvalidDataException("Platform profile ids must be unique.");
        }

        return new PlatformProfiles(profiles);
    }

    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        // The wire words are snake case ("structured_review", "medium"); the enums are PascalCase in code.
        options.Converters.Add(new JsonStringEnumConverter<DraftFamily>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<PlatformRisk>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        return options;
    }

    /// <summary>The root object of the file: one array of profiles.</summary>
    private sealed record ProfilesFile(IReadOnlyList<PlatformProfile> Profiles);

    private static string ReadResource(string name)
    {
        using var s = typeof(PlatformProfiles).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded resource '{name}'.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
