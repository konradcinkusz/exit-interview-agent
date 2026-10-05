namespace ExitInterviewAgent.Privacy;

/// <summary>What kind of personal data a finding is. Kinds only: a finding never carries the matched text.</summary>
public enum PiiKind
{
    Email,
    Url,
    IpAddress,
    NationalId,
    EmployeeId,
    Phone,
    Handle,
    PersonName,
}

/// <summary>Why a span was flagged. <see cref="FailClosed"/> spans exist only because uncertainty is resolved by masking.</summary>
public enum PiiBasis
{
    /// <summary>A structural pattern (email, checksum-valid id, ...).</summary>
    Pattern,

    /// <summary>A language heuristic (title, relation construction, capitalised tokens, first-name lexicon).</summary>
    Heuristic,

    /// <summary>Masked only because <see cref="PiiOptions.FailClosed"/> is on.</summary>
    FailClosed,
}

/// <summary>
/// A detected span: kind and UTF-16 offsets into the ORIGINAL text. Deliberately has no text member, so a finding
/// can be logged, traced or stored without leaking what it points at.
/// </summary>
public readonly record struct PiiFinding(PiiKind Kind, int Start, int Length, PiiBasis Basis)
{
    public int End => Start + Length;
}

public sealed record MaskResult(string MaskedText, IReadOnlyList<PiiFinding> Findings)
{
    public bool HasFindings => Findings.Count > 0;
}

public sealed record PiiOptions
{
    /// <summary>
    /// Employer and product names that must not be taken for people, matched case-insensitively on word boundaries
    /// (entries of five or more characters also match a short inflectional ending, e.g. Polish case endings).
    /// Applies to person-name detection only; an email or URL containing such a name is still masked.
    /// </summary>
    public IReadOnlyCollection<string> AllowList { get; init; } = [];

    /// <summary>
    /// When uncertain, mask: also mask unrecognised capitalised words in mid-sentence, ambiguous first names at the
    /// start of a sentence, id-shaped digit runs whose checksum fails, and long bare digit runs.
    /// Raises recall at a measured cost in precision (docs/privacy/pii-detector.md).
    /// </summary>
    public bool FailClosed { get; init; }
}
