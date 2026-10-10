using ExitInterviewAgent.InterviewService.Persistence;

namespace ExitInterviewAgent.InterviewService.Interviews;

/// <summary>
/// The one entry point to credits. A start consumes one credit through this gate; the payments ledger (Billing/, ADR-0077) is the
/// implementation when <c>Interviews:RequireCredit</c> is true. The session id is passed so that the consumption can be recorded against it.
/// </summary>
public interface ICreditGate
{
    ValueTask<bool> TryConsumeAsync(string accountId, string sessionId, CancellationToken ct);
}

/// <summary>
/// Settles a session once, when it ends (W11, ADR-0077 implementation notes). The settlement row is written with the outcome; a
/// failed or lost session also gets its credit back, in the same transaction. Returns true only for the call that wrote the row.
/// </summary>
public interface ICreditSettlement
{
    ValueTask<bool> SettleAsync(string accountId, string sessionId, SessionOutcome outcome, CancellationToken ct);
}

/// <summary>The default while <c>Interviews:RequireCredit</c> is false: every start is allowed (development and the pre-payment build).</summary>
public sealed class AllowAllCreditGate : ICreditGate
{
    public ValueTask<bool> TryConsumeAsync(string accountId, string sessionId, CancellationToken ct) => ValueTask.FromResult(true);
}

/// <summary>The settlement while credits are not required: there is no ledger to write to.</summary>
public sealed class NoopCreditSettlement : ICreditSettlement
{
    public ValueTask<bool> SettleAsync(string accountId, string sessionId, SessionOutcome outcome, CancellationToken ct) => ValueTask.FromResult(false);
}
