namespace ExitInterviewAgent.Signals;

/// <summary>How far the employer claim was checked at submission time. The signals module's own type: nothing is shared with the submission side.</summary>
public enum VerificationState { Unchecked = 0, Unverified = 1, Verified = 2 }

/// <summary>One topic of one record: covered topics only. A null rating is a covered topic without a number; it counts as a respondent, never as a rating.</summary>
public sealed record TopicRating(string Topic, int? Rating);

/// <summary>
/// The whole input of the module: one record reduced to what the aggregation rules read (ADR-0052). No identifier, no quote, no
/// confidence, no time, no free text: nothing that could identify a record, and nothing the rules do not use. Bands are the
/// wire names of ADR-0007; topics are the wire names of the record schema.
/// </summary>
public sealed record Observation(
    string EmployerRef,
    string TenureBand,
    string? SeniorityBand,
    string? FunctionBand,
    VerificationState Verification,
    IReadOnlyList<TopicRating> Ratings);

/// <summary>
/// The input port. A transport implements it: today an in-process adapter on the interview-service's record store, later an
/// HTTP or event feed when the module is extracted (docs/architecture/signals.md). The contract is deliberately small:
/// <list type="bullet">
///   <item>Observations of one employer are contiguous (any order of employers, any order inside one employer).</item>
///   <item>The feed is a full snapshot of what is stored now: a deleted record is simply absent the next time. The module keeps no
///   record-level state, so there is nothing to retract.</item>
///   <item>Memory use is the source's to bound: it streams.</item>
/// </list>
/// </summary>
public interface IObservationSource
{
    IAsyncEnumerable<Observation> ReadAsync(CancellationToken ct);
}

/// <summary>Optional gate the host may register: the publisher waits for it before its first run (used by the demo seeder so a demo never publishes half a seed).</summary>
public interface IPublicationGate
{
    Task WaitAsync(CancellationToken ct);
}
