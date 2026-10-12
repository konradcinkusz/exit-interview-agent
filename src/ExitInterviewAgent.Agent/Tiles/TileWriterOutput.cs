using System.Collections.Frozen;
using System.Text.Json;
using System.Text.RegularExpressions;
using LateApexEarlySpeed.Json.Schema;
using LateApexEarlySpeed.Json.Schema.Common;

namespace ExitInterviewAgent.Agent.Tiles;

/// <summary>
/// Untrusted writer output, validated against <c>schemas/tile-writer-output.v2.schema.json</c> and mapped to candidates. A family
/// kind keeps its sections; its text is the sections joined by a blank line (ADR-0083).
/// Every failure is reported as <see cref="TileDropReason.SchemaInvalid"/>, a code only: a parse or schema message can
/// quote the text it rejected, so none is kept. A reply with one bad tile is rejected whole; the retry asks again.
/// </summary>
public sealed class TileWriterOutput
{
    private const int MaxBytes = 64 * 1024;

    private static readonly FrozenDictionary<string, TileKind> Kinds = new Dictionary<string, TileKind>
    {
        ["overview"] = TileKind.Overview,
        ["what_worked"] = TileKind.WhatWorked,
        ["what_could_improve"] = TileKind.WhatCouldImprove,
        ["for_the_next_person"] = TileKind.ForTheNextPerson,
        ["structured_review"] = TileKind.StructuredReview,
        ["narrative"] = TileKind.Narrative,
        ["professional_post"] = TileKind.ProfessionalPost,
        ["short_post"] = TileKind.ShortPost,
    }.ToFrozenDictionary();

    public static string SchemaText { get; } = LoadSchema();

    private static readonly JsonValidator Validator = new(SchemaText);

    public IReadOnlyList<CandidateTile> Tiles { get; }

    private TileWriterOutput(IReadOnlyList<CandidateTile> tiles) => Tiles = tiles;

    /// <summary>The schema's name for a model-written kind. <see cref="TileKind.Facts"/> has none: the model never writes it.</summary>
    public static string KindName(TileKind kind) =>
        Kinds.FirstOrDefault(p => p.Value == kind).Key ?? throw new ArgumentOutOfRangeException(nameof(kind), "The model never writes this kind.");

    public static bool TryParse(string? modelText, out TileWriterOutput? output, out IReadOnlyList<string> errorCodes)
    {
        output = null;
        var json = Isolate(modelText);
        if (json is null || json.Length > MaxBytes) { errorCodes = [TileDropReason.SchemaInvalid]; return false; }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8, AllowDuplicateProperties = false }); }
        catch (JsonException) { errorCodes = [TileDropReason.SchemaInvalid]; return false; }

        using (doc)
        {
            ValidationResult result;
            try
            {
                result = Validator.Validate(doc.RootElement, new JsonSchemaOptions { OutputFormat = OutputFormat.List, GenerateErrorMessages = false, ValidateFormat = false, RegexMatchTimeout = TimeSpan.FromMilliseconds(100) });
            }
            catch (RegexMatchTimeoutException) { errorCodes = [TileDropReason.SchemaInvalid]; return false; }
            if (!result.IsValid) { errorCodes = [TileDropReason.SchemaInvalid]; return false; }

            var tiles = new List<CandidateTile>();
            foreach (var e in doc.RootElement.GetProperty("tiles").EnumerateArray())
            {
                var candidate = Map(e);
                if (candidate is null) { errorCodes = [TileDropReason.SchemaInvalid]; return false; }
                tiles.Add(candidate);
            }
            if (tiles.Select(t => t.Kind).Distinct().Count() != tiles.Count) { errorCodes = [TileDropReason.SchemaInvalid]; return false; }

            output = new TileWriterOutput(tiles);
            errorCodes = [];
            return true;
        }
    }

    /// <summary>
    /// One schema-valid tile element as a candidate. A generic kind carries its title and text. A family kind carries its sections:
    /// the title is the <c>title</c> section (empty when there is none), and the text is the sections joined by a blank line.
    /// Returns null when a section key repeats, which the schema alone does not forbid.
    /// </summary>
    private static CandidateTile? Map(JsonElement e)
    {
        var kind = Kinds[e.GetProperty("kind").GetString()!];
        var basedOn = e.GetProperty("basedOn").EnumerateArray().Select(b => b.GetString()!).ToArray();
        if (TileFamilies.Find(kind) is null)
            return new CandidateTile(kind, e.GetProperty("title").GetString()!, e.GetProperty("text").GetString()!, basedOn);

        var sections = e.GetProperty("sections").EnumerateArray()
            .Select(s => new TileSection(s.GetProperty("key").GetString()!, s.GetProperty("text").GetString()!))
            .ToArray();
        if (sections.Select(s => s.Key).Distinct().Count() != sections.Length) return null;

        var title = sections.FirstOrDefault(s => s.Key == "title")?.Text ?? string.Empty;
        var text = string.Join("\n\n", sections.Select(s => s.Text));
        return new CandidateTile(kind, title, text, basedOn, sections);
    }

    /// <summary>Takes the outermost JSON object out of a reply that may be wrapped in a code fence or a sentence.</summary>
    private static string? Isolate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    private static string LoadSchema()
    {
        using var s = typeof(TileWriterOutput).Assembly.GetManifestResourceStream("tile-writer-output.v2.schema.json")
            ?? throw new InvalidOperationException("Embedded tile writer schema is missing.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
