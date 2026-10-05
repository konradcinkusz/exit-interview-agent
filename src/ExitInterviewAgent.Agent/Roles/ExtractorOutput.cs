using System.Text.Json;
using System.Text.RegularExpressions;
using ExitInterviewAgent.Records;
using LateApexEarlySpeed.Json.Schema;
using LateApexEarlySpeed.Json.Schema.Common;

namespace ExitInterviewAgent.Agent.Roles;

/// <summary>One topic as the extractor reported it, after schema validation.</summary>
public sealed record ExtractedTopic(bool Covered, int? Rating, Confidence? Confidence, IReadOnlyList<string> Quotes);

public static class ExtractorErrors
{
    public const string NotJson = "extractor.not_json";
    public const string SchemaViolation = "extractor.schema_violation";
}

/// <summary>
/// Untrusted model output, validated against <c>schemas/extractor-output.v1.schema.json</c> and mapped to typed values.
/// Errors are codes only: a parse or schema message can quote the text it rejected.
/// </summary>
public sealed class ExtractorOutput
{
    private const int MaxBytes = 64 * 1024;

    public static string SchemaText { get; } = LoadSchema();

    private static readonly JsonValidator Validator = new(SchemaText);

    public IReadOnlyDictionary<Topic, ExtractedTopic> Topics { get; }

    private ExtractorOutput(IReadOnlyDictionary<Topic, ExtractedTopic> topics) => Topics = topics;

    public static bool TryParse(string? modelText, out ExtractorOutput? output, out IReadOnlyList<string> errorCodes)
    {
        output = null;
        var json = Isolate(modelText);
        if (json is null || json.Length > MaxBytes) { errorCodes = [ExtractorErrors.NotJson]; return false; }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8, AllowDuplicateProperties = false }); }
        catch (JsonException) { errorCodes = [ExtractorErrors.NotJson]; return false; }

        using (doc)
        {
            ValidationResult result;
            try
            {
                result = Validator.Validate(doc.RootElement, new JsonSchemaOptions { OutputFormat = OutputFormat.List, GenerateErrorMessages = false, ValidateFormat = false, RegexMatchTimeout = TimeSpan.FromMilliseconds(100) });
            }
            catch (RegexMatchTimeoutException) { errorCodes = [ExtractorErrors.SchemaViolation]; return false; }
            if (!result.IsValid) { errorCodes = [ExtractorErrors.SchemaViolation]; return false; }

            var topics = new Dictionary<Topic, ExtractedTopic>();
            foreach (var topic in Enum.GetValues<Topic>())
            {
                var e = doc.RootElement.GetProperty("topics").GetProperty(Wire.Name(topic));
                var covered = e.GetProperty("status").GetString() == "covered";
                if (!covered) { topics[topic] = new ExtractedTopic(false, null, null, []); continue; }
                int? rating = e.GetProperty("rating") is { ValueKind: JsonValueKind.Number } r ? r.GetInt32() : null;
                Wire.TryParse<Confidence>(e.GetProperty("confidence").GetString(), out var confidence);
                topics[topic] = new ExtractedTopic(true, rating, confidence, e.GetProperty("quotes").EnumerateArray().Select(q => q.GetString()!).ToArray());
            }
            output = new ExtractorOutput(topics);
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
        using var s = typeof(ExtractorOutput).Assembly.GetManifestResourceStream("extractor-output.v1.schema.json")
            ?? throw new InvalidOperationException("Embedded extractor schema is missing.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
