using System.Collections.Frozen;
using System.Text.Json;
using System.Text.RegularExpressions;
using LateApexEarlySpeed.Json.Schema;
using LateApexEarlySpeed.Json.Schema.Common;

namespace ExitInterviewAgent.Agent.Tiles;

/// <summary>
/// Untrusted writer output, validated against <c>schemas/tile-writer-output.v1.schema.json</c> and mapped to candidates.
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
        ["short_note"] = TileKind.ShortNote,
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
                var kind = Kinds[e.GetProperty("kind").GetString()!];
                var basedOn = e.GetProperty("basedOn").EnumerateArray().Select(b => b.GetString()!).ToArray();
                tiles.Add(new CandidateTile(kind, e.GetProperty("title").GetString()!, e.GetProperty("text").GetString()!, basedOn));
            }
            if (tiles.Select(t => t.Kind).Distinct().Count() != tiles.Count) { errorCodes = [TileDropReason.SchemaInvalid]; return false; }

            output = new TileWriterOutput(tiles);
            errorCodes = [];
            return true;
        }
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
        using var s = typeof(TileWriterOutput).Assembly.GetManifestResourceStream("tile-writer-output.v1.schema.json")
            ?? throw new InvalidOperationException("Embedded tile writer schema is missing.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
