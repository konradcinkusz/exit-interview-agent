using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LateApexEarlySpeed.Json.Schema;
using LateApexEarlySpeed.Json.Schema.Common;

namespace ExitInterviewAgent.Records;

/// <summary>
/// Validates untrusted submitted records against schema v1 and maps valid ones to <see cref="InterviewRecord"/>.
/// Size limit, depth limit and duplicate-key rejection run before the schema; errors are codes and paths only.
/// Instances are immutable after construction and safe to share.
/// </summary>
public sealed class RecordValidator
{
    private readonly RecordLimits _limits;
    private readonly JsonValidator _validator = new(RecordSchema.JsonText);

    public RecordValidator(RecordLimits? limits = null) => _limits = limits ?? RecordLimits.Default;

    public ValidationOutcome Validate(string json) =>
        Validate(Encoding.UTF8.GetBytes(json ?? throw new ArgumentNullException(nameof(json))));

    public ValidationOutcome Validate(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length > _limits.MaxPayloadBytes) return ValidationOutcome.Fail(RecordErrorCodes.PayloadTooLarge);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(utf8Json.ToArray(), new JsonDocumentOptions { MaxDepth = _limits.MaxDepth, AllowDuplicateProperties = false });
        }
        catch (JsonException)
        {
            return ValidationOutcome.Fail(Classify(utf8Json));
        }

        using (doc)
        {
            var root = doc.RootElement;
            var options = new JsonSchemaOptions
            {
                OutputFormat = OutputFormat.List,
                GenerateErrorMessages = false,
                ValidateFormat = false,
                RegexMatchTimeout = _limits.RegexTimeout,
            };

            ValidationResult result;
            try { result = _validator.Validate(root, options); }
            catch (RegexMatchTimeoutException) { return ValidationOutcome.Fail(RecordErrorCodes.ValidationTimeout); }

            if (!result.IsValid) return ToOutcome(result);

            var record = RecordReader.Read(root);
            if (_limits.RequirePiiMasked && !record.PiiMasked)
                return new ValidationOutcome(null, [new RecordError(RecordErrorCodes.PiiNotMasked, "/piiMasked")]);
            if (_limits.RequireAiDisclosed && !record.Interview.AiDisclosed)
                return new ValidationOutcome(null, [new RecordError(RecordErrorCodes.AiNotDisclosed, "/interview/aiDisclosed")]);
            return new ValidationOutcome(record, []);
        }
    }

    /// <summary>Tells depth and duplicate-key failures apart from plain malformed JSON, without reading any message.</summary>
    private string Classify(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            using var _ = JsonDocument.Parse(utf8Json.ToArray(), new JsonDocumentOptions { MaxDepth = _limits.MaxDepth, AllowDuplicateProperties = true });
            return RecordErrorCodes.DuplicateKey;
        }
        catch (JsonException)
        {
            try
            {
                using var _ = JsonDocument.Parse(utf8Json.ToArray(), new JsonDocumentOptions { MaxDepth = 4096, AllowDuplicateProperties = true });
                return RecordErrorCodes.NestingTooDeep;
            }
            catch (JsonException)
            {
                return RecordErrorCodes.NotJson;
            }
        }
    }

    private ValidationOutcome ToOutcome(ValidationResult result)
    {
        var errors = new List<RecordError>();
        var seen = new HashSet<RecordError>();
        var truncated = false;
        foreach (var e in result.ValidationErrors ?? [])
        {
            // A failing `if` condition only selects the then/else branch; it is not a violation of the record.
            if (e.RelativeKeywordLocation?.ToString().Contains("/if/", StringComparison.Ordinal) == true) continue;
            var keyword = KeywordOf(e);
            var location = e.InstanceLocation?.ToString() ?? "";
            // For additionalProperties the library points at the offending key itself: report its parent, never the key.
            if (keyword == "additionalProperties") location = location[..Math.Max(0, location.LastIndexOf('/'))];
            var error = new RecordError(CodeFor(e, keyword), SafePath(location));
            if (!seen.Add(error)) continue;
            if (errors.Count >= _limits.MaxErrors) { truncated = true; break; }
            errors.Add(error);
        }
        if (errors.Count == 0) errors.Add(new RecordError(RecordErrorCodes.SchemaViolation, ""));
        return new ValidationOutcome(null, errors, truncated);
    }

    private static string KeywordOf(ValidationError e)
    {
        if (!string.IsNullOrEmpty(e.Keyword)) return e.Keyword;
        var where = e.RelativeKeywordLocation?.ToString() ?? "";
        return where[(where.LastIndexOf('/') + 1)..];
    }

    private static string CodeFor(ValidationError e, string keyword)
    {
        var where = e.RelativeKeywordLocation?.ToString() ?? "";
        var path = e.InstanceLocation?.ToString() ?? "";
        if (where.Contains("/then/", StringComparison.Ordinal) || where.Contains("/else/", StringComparison.Ordinal))
            return RecordErrorCodes.TopicInconsistent;
        return keyword switch
        {
            "required" => RecordErrorCodes.MissingField,
            "additionalProperties" => RecordErrorCodes.UnknownField,
            "type" => RecordErrorCodes.WrongType,
            "const" when path == "/schemaVersion" => RecordErrorCodes.UnsupportedSchemaVersion,
            "enum" or "const" => RecordErrorCodes.ValueNotAllowed,
            "pattern" => RecordErrorCodes.BadFormat,
            "minimum" or "maximum" => RecordErrorCodes.OutOfRange,
            "minLength" or "maxLength" or "minItems" or "maxItems" => RecordErrorCodes.LengthLimit,
            _ => RecordErrorCodes.SchemaViolation,
        };
    }

    /// <summary>Keeps only schema-defined names and small array indexes; anything else (a submitted key) becomes "*".</summary>
    internal static string SafePath(string? pointer)
    {
        if (string.IsNullOrEmpty(pointer)) return "";
        var sb = new StringBuilder();
        foreach (var raw in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = raw.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            var ok = RecordSchema.KnownNames.Contains(segment) || (segment.Length <= 3 && segment.All(char.IsAsciiDigit));
            sb.Append('/').Append(ok ? segment : "*");
        }
        return sb.ToString();
    }
}
