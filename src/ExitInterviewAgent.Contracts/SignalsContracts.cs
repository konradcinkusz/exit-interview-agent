namespace ExitInterviewAgent.Contracts;

// Employer signals: aggregates only (brief section 6, ADR-0053). Nothing here can carry a record, a quote or an interview id, and
// nothing here ranks, scores or compares employers. Every number travels with its sample size and its interval in the same object.

/// <summary>Stable machine-readable codes of the signals endpoints. Never renamed or reused.</summary>
public static class SignalsCodes
{
    /// <summary>No displayable cell for this employer in the published snapshot. An unknown employer and one with too few records get this same answer.</summary>
    public const string EmployerNotFound = "SIGNALS_EMPLOYER_NOT_FOUND";
    public const string InvalidEmployerRef = "SIGNALS_INVALID_EMPLOYER_REF";
}

/// <summary>Values of the closed vocabularies of the signals payload.</summary>
public static class SignalsVocabulary
{
    public const string TopicOk = "ok";
    public const string TopicInsufficientData = "insufficient_data";
    public const string CutPublished = "published";
    public const string CutSuppressed = "suppressed";
    public const string BandOk = "ok";
    public const string BandNone = "none";
    public const string BandSuppressed = "suppressed";
    public const string ReliabilityLow = "low";
    public const string ReliabilityModerate = "moderate";
    public const string ReliabilityHigh = "high";
    public const string CoverageHigh = "high";
    public const string CoverageMedium = "medium";
    public const string CoverageLow = "low";
}

/// <summary>
/// Which snapshot the numbers come from. <see cref="GeneratedAt"/> is the START of the batch period, deliberately coarse (never the
/// moment the run finished, never finer than an hour). The snapshot is rebuilt from what is stored once per period, not on
/// submission: a record deleted after publication still counts until the next snapshot (<see cref="DeletionsAppearAtNextPublication"/>).
/// </summary>
public sealed record SignalsSnapshotInfo(
    DateTimeOffset GeneratedAt,
    int PublicationIntervalHours,
    string RulesVersion,
    int MinimumGroupSize,
    bool DeletionsAppearAtNextPublication);

/// <summary>Employers with at least one displayable cell, alphabetical. No scores, no order by anything but the reference.</summary>
public sealed record SignalsEmployerList(SignalsSnapshotInfo? Snapshot, IReadOnlyList<string> Employers, int Page, int Limit, int Total);

/// <summary>One employer: six topics, each standing alone (there is no composite and no cross-topic average).</summary>
public sealed record SignalsEmployer(SignalsSnapshotInfo Snapshot, string EmployerRef, string RespondentsBand, IReadOnlyList<SignalsTopic> Topics);

/// <summary><see cref="Status"/> is <c>ok</c> or <c>insufficient_data</c>; the latter carries no number and says nothing about how many records exist.</summary>
public sealed record SignalsTopic(string Topic, string Status, SignalsStats? Overall, IReadOnlyList<SignalsCut> Cuts);

/// <summary>One band at a time (tenure, seniority, function): never a cross-product. <c>suppressed</c> withholds the whole cut, by design.</summary>
public sealed record SignalsCut(string Dimension, string Status, IReadOnlyList<SignalsBandCell> Cells);

/// <summary><c>ok</c> (with stats), <c>none</c> (nobody in this band for this topic) or <c>suppressed</c>.</summary>
public sealed record SignalsBandCell(string Band, string Status, SignalsStats? Stats);

/// <summary>
/// A displayed cell. <c>N</c> is the number of ratings, always at least the minimum group size. Read mean and interval together: a wide interval
/// means early, not wrong. <c>Coverage</c> is the counter-metric, the share of the cell's population that rated the topic. Distribution and
/// verification are present only when every group in them is empty or at least the minimum group size.
/// </summary>
public sealed record SignalsStats(
    int N,
    double Mean,
    SignalsInterval Interval,
    string Reliability,
    string Coverage,
    IReadOnlyList<SignalsGroup>? Distribution,
    IReadOnlyList<SignalsGroup>? Verification);

public sealed record SignalsInterval(double Lower, double Upper, double Level, string Method);

/// <summary>A bin of the rating distribution (<c>low</c> 1-2, <c>mid</c> 3, <c>high</c> 4-5) or a verification level (<c>unchecked</c>, <c>unverified</c>, <c>verified</c>).</summary>
public sealed record SignalsGroup(string Key, int Count);
