using ExitInterviewAgent.InterviewService.Interviews;
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
/// The W2 refund seam (<see cref="ICreditRefund"/>) backed by the ledger: the credit of a session that ended as failed comes back
/// once. Called by the session once per session; a failure is logged by the caller (type only) and the session still ends.
/// </summary>
public sealed class LedgerCreditRefund(CreditLedger ledger) : ICreditRefund
{
    public async ValueTask RefundAsync(string accountId, string sessionId, CancellationToken ct) =>
        await ledger.RefundAsync(accountId, sessionId, ct).ConfigureAwait(false);
}
