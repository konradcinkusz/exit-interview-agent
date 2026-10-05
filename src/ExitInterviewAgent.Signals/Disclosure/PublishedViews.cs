namespace ExitInterviewAgent.Signals;

// What a snapshot holds and what the API reads back. Everything here has already passed the disclosure rules: this is the only
// shape in which any aggregate exists outside the memory of one publication run, and there is no field in it that could hold
// a record, a quote, an interview id or a time finer than a batch.

/// <summary>A count in a published group: a bin of the rating distribution or a verification level. Never below k.</summary>
public sealed record GroupView(string Key, int Count);

/// <summary>
/// The statistics of one displayed cell. <see cref="Coverage"/> is the counter-metric: the share of the cell's population that
/// gave a rating, banded, in the same payload as the mean. <see cref="Distribution"/> and <see cref="Verification"/> are present
/// only on an employer x topic cell and only when each of their groups is empty or at least k.
/// </summary>
public sealed record StatsView(
    int N,
    double Mean,
    double Lower,
    double Upper,
    string Reliability,
    string Coverage,
    IReadOnlyList<GroupView>? Distribution,
    IReadOnlyList<GroupView>? Verification);

/// <summary>One band of a single-band cut. <see cref="Status"/> is <c>ok</c> (with stats) or <c>none</c> (empty band, no stats).</summary>
public sealed record BandView(string Band, string Status, StatsView? Stats);

/// <summary>A single-band cut. Present with cells only when the whole cut is a clean partition (ADR-0053); otherwise <see cref="Cells"/> is empty.</summary>
public sealed record CutView(string Dimension, bool Published, IReadOnlyList<BandView> Cells);

/// <summary>One topic of one employer. <see cref="Overall"/> is null (insufficient data) when fewer than k records rated the topic.</summary>
public sealed record TopicView(string Topic, StatsView? Overall, IReadOnlyList<CutView> Cuts);

/// <summary>An employer with at least one displayable topic. The respondent count appears only as a band whose lower edge is at least k.</summary>
public sealed record EmployerView(string EmployerRef, string RespondentsBand, IReadOnlyList<TopicView> Topics);

public static class BandStatuses
{
    public const string Ok = "ok";
    public const string None = "none";
}
