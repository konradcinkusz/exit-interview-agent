namespace ExitInterviewAgent.Signals.Persistence;

/// <summary>
/// One published snapshot. The row is inserted LAST, after its employer rows, and that single insert is the publication: readers
/// follow the highest <see cref="Seq"/>, so a run that dies half way leaves invisible rows that the next run removes. Only the
/// start of the batch period is stored, never the moment the run happened to finish.
/// </summary>
public sealed class SnapshotRow
{
    public required long Seq { get; init; }
    public required Guid Id { get; init; }
    public required string Fingerprint { get; init; }
    public required DateTimeOffset PeriodStart { get; init; }
    public required int IntervalHours { get; init; }
    public required string RulesVersion { get; init; }
    public required int MinimumGroupSize { get; init; }
    public required int EmployerCount { get; init; }
}

/// <summary>
/// One displayable employer of one snapshot: the employer reference and its already-controlled view as JSON. There is deliberately no
/// numeric column that a query could sort by, so "order employers by score" is not expressible against this store (metric-ethics §1).
/// </summary>
public sealed class EmployerSnapshotRow
{
    public required Guid SnapshotId { get; init; }
    public required string EmployerRef { get; init; }
    public required string View { get; init; }
}
