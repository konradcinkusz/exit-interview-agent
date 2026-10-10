using ExitInterviewAgent.InterviewService.Interviews;
using ExitInterviewAgent.InterviewService.Persistence;
using Microsoft.Extensions.Logging;

namespace ExitInterviewAgent.InterviewService.Billing;

/// <summary>
/// The W2 gate (<see cref="ICreditGate"/>) backed by the ledger: a start takes one credit or is refused. A ledger failure is a
/// refusal, never a free start (fail closed); the log names the exception type only.
/// </summary>
public sealed class LedgerCreditGate(CreditLedger ledger, ILogger<LedgerCreditGate> logger) : ICreditGate
{
    public async ValueTask<bool> TryConsumeAsync(string accountId, string sessionId, CancellationToken ct)
    {
        try
        {
            return await ledger.ConsumeAsync(accountId, sessionId, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError("Credit consume failed, start refused: {ExceptionType}", e.GetType().Name);
            return false;
        }
    }
}

/// <summary>
/// The settlement seam (<see cref="ICreditSettlement"/>) backed by the ledger (W11): a session's settlement row and, for a failed or
/// lost session, its refund, in one transaction. Called by the session once when it ends; a failure is logged by the caller
/// (type only) and the session still ends.
/// </summary>
public sealed class LedgerCreditSettlement(CreditLedger ledger) : ICreditSettlement
{
    public ValueTask<bool> SettleAsync(string accountId, string sessionId, SessionOutcome outcome, CancellationToken ct) =>
        new(ledger.SettleAsync(accountId, sessionId, outcome, ct));
}
