namespace ExitInterviewAgent.Records;

/// <summary>
/// Stable machine-readable error codes. Part of the public contract: codes are never renamed or reused;
/// new ones may be added. Errors never carry submitted text (it may contain personal data).
/// </summary>
public static class RecordErrorCodes
{
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
    public const string NotJson = "NOT_JSON";
    public const string DuplicateKey = "DUPLICATE_KEY";
    public const string NestingTooDeep = "NESTING_TOO_DEEP";
    public const string UnsupportedSchemaVersion = "UNSUPPORTED_SCHEMA_VERSION";
    public const string MissingField = "MISSING_FIELD";
    public const string UnknownField = "UNKNOWN_FIELD";
    public const string WrongType = "WRONG_TYPE";
    public const string ValueNotAllowed = "VALUE_NOT_ALLOWED";
    public const string BadFormat = "BAD_FORMAT";
    public const string OutOfRange = "OUT_OF_RANGE";
    public const string LengthLimit = "LENGTH_LIMIT";
    public const string TopicInconsistent = "TOPIC_INCONSISTENT";
    public const string PiiNotMasked = "PII_NOT_MASKED";
    public const string ValidationTimeout = "VALIDATION_TIMEOUT";
    public const string SchemaViolation = "SCHEMA_VIOLATION";
    public const string QuoteNotVerbatim = "QUOTE_NOT_VERBATIM";

    public static IReadOnlyList<string> All { get; } =
    [
        PayloadTooLarge, NotJson, DuplicateKey, NestingTooDeep, UnsupportedSchemaVersion, MissingField, UnknownField,
        WrongType, ValueNotAllowed, BadFormat, OutOfRange, LengthLimit, TopicInconsistent, PiiNotMasked,
        ValidationTimeout, SchemaViolation, QuoteNotVerbatim,
    ];
}

/// <summary>
/// One validation failure: a stable <see cref="Code"/> and a JSON-pointer-like <see cref="Path"/> made only of
/// field names defined by the schema and array indexes. Unknown submitted keys are reported as the parent path.
/// </summary>
public sealed record RecordError(string Code, string Path);

public sealed record RecordLimits
{
    public static RecordLimits Default { get; } = new();

    /// <summary>Largest accepted payload in bytes. The schema's own caps bound a valid record far below this.</summary>
    public int MaxPayloadBytes { get; init; } = 32 * 1024;

    /// <summary>Deepest accepted JSON nesting. A valid record nests 5 levels.</summary>
    public int MaxDepth { get; init; } = 8;

    /// <summary>Most errors reported; the rest are dropped (see <see cref="ValidationOutcome.ErrorsTruncated"/>).</summary>
    public int MaxErrors { get; init; } = 50;

    /// <summary>Bound on regex evaluation inside schema validation (denial-of-service guard).</summary>
    public TimeSpan RegexTimeout { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Ingest policy: reject records whose quotes were not taken from a PII-masked transcript.</summary>
    public bool RequirePiiMasked { get; init; } = true;
}

public sealed record ValidationOutcome(InterviewRecord? Record, IReadOnlyList<RecordError> Errors, bool ErrorsTruncated = false)
{
    public bool IsValid => Record is not null && Errors.Count == 0;

    internal static ValidationOutcome Fail(string code, string path = "") => new(null, [new RecordError(code, path)]);
}
