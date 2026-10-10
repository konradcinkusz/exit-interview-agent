namespace ExitInterviewAgent.InterviewService.Interviews;

/// <summary>
/// The one entry point to credits. A start consumes one credit through this gate; the payments ledger (Billing/, ADR-0077) is the
/// implementation when <c>Interviews:RequireCredit</c> is true. The session id is passed so that the consumption can be recorded against it.
/// </summary>
public interface ICreditGate
{
    ValueTask<bool> TryConsumeAsync(string accountId, string sessionId, CancellationToken ct);
}

/// <summary>Returns a credit when a session ends as <c>failed</c> (a service fault). Never called for a withdrawal.</summary>
public interface ICreditRefund
{
    ValueTask RefundAsync(string accountId, string sessionId, CancellationToken ct);
}

/// <summary>The default while <c>Interviews:RequireCredit</c> is false: every start is allowed (development and the pre-payment build).</summary>
public sealed class AllowAllCreditGate : ICreditGate
{
    public ValueTask<bool> TryConsumeAsync(string accountId, string sessionId, CancellationToken ct) => ValueTask.FromResult(true);
}

/// <summary>The refund while credits are not required: there is nothing to return.</summary>
public sealed class NoopCreditRefund : ICreditRefund
{
    public ValueTask RefundAsync(string accountId, string sessionId, CancellationToken ct) => ValueTask.CompletedTask;
}
