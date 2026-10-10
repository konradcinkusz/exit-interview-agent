namespace ExitInterviewAgent.InterviewService.Persistence;

/// <summary>Why a credit row exists. A row is appended, never updated: the balance is the sum of the deltas.</summary>
public enum CreditReason
{
    /// <summary>A verified payment added credits. <see cref="CreditEntry.Reference"/> is the provider event id.</summary>
    Purchase = 0,

    /// <summary>A started interview took one credit. <see cref="CreditEntry.Reference"/> is the session id.</summary>
    Consume = 1,

    /// <summary>A session that ended as failed (a service fault) returned its credit. <see cref="CreditEntry.Reference"/> is the session id.</summary>
    Refund = 2,

    /// <summary>
    /// A session that was open when its process stopped (no settlement row, idle longer than its window) returned its credit at the
    /// startup sweep (W11). <see cref="CreditEntry.Reference"/> is the session id.
    /// </summary>
    RefundLostSession = 3,
}

/// <summary>How a session ended, as stored in its settlement row (W11). The words are the contract's, plus <c>lost</c>.</summary>
public enum SessionOutcome
{
    /// <summary>The interview reached its record. The credit stays spent.</summary>
    Completed = 0,

    /// <summary>The person deleted an open interview. The credit stays spent (their choice).</summary>
    Withdrawn = 1,

    /// <summary>Consent was refused or withdrawn, or the idle window ended the interview. The credit stays spent.</summary>
    Stopped = 2,

    /// <summary>A service fault ended the interview. The credit came back in the same transaction.</summary>
    Failed = 3,

    /// <summary>The process stopped with the session open. The startup sweep settled it and returned the credit.</summary>
    Lost = 4,
}

/// <summary>
/// One line of the credit ledger (append-only, web-app-plan §4). The account is the only subject held here, because a credit
/// belongs to an account; the row links to no record, no transcript and no employer. Payment data (card, billing address) never
/// reaches this table. The unique index on (<see cref="Reason"/>, <see cref="Reference"/>) makes one purchase, one consume and
/// one refund per event or session a database fact, not a code convention.
/// </summary>
public sealed class CreditEntry
{
    public required Guid Id { get; init; }
    public required string AccountRef { get; init; }
    /// <summary>+N for a purchase or refund, -1 for a consume.</summary>
    public required int Delta { get; init; }
    public required CreditReason Reason { get; init; }
    public string? Reference { get; init; }
    /// <summary>Monday of the ISO week the row was written (ADR-0027). Never finer.</summary>
    public required DateOnly CreatedWeek { get; init; }

    /// <summary>
    /// On a consume row only: the start of the UTC hour the session started in (W11). The startup sweep needs to know that a session
    /// is older than its idle window, and an hour is the coarsest bucket that allows it. It is never a finer time.
    /// </summary>
    public DateTimeOffset? StartedHour { get; init; }
}

/// <summary>
/// The settlement of one session (W11, ADR-0077 implementation notes), written when the session ends and keyed by the session id
/// (UNIQUE): the first writer settles it, every later one finds the row and does nothing. It holds the account (the same subject
/// the credit rows hold), the outcome and the ISO week. It holds no interview text, no record and no time finer than a week.
/// </summary>
public sealed class SessionSettlementRow
{
    public required Guid Id { get; init; }
    public required string SessionId { get; init; }
    public required string AccountRef { get; init; }
    public required SessionOutcome Outcome { get; init; }
    /// <summary>Monday of the ISO week the session was settled. Never finer.</summary>
    public required DateOnly SettledWeek { get; init; }
}

/// <summary>
/// A payment event the service has accepted, keyed by the provider's event id (UNIQUE): a redelivered webhook finds its row and
/// adds nothing (idempotency, PAYMENTS-AND-MONETIZATION §4). It stores the outcome and the amount expected and received, never the
/// webhook body and never anything the payer typed.
/// </summary>
public sealed class PaymentEventRow
{
    public required Guid Id { get; init; }
    public required string ProviderEventId { get; init; }
    public required string Kind { get; init; }
    public required string AccountRef { get; init; }
    public required int Quantity { get; init; }
    public required long AmountMinorUnits { get; init; }
    public required string Currency { get; init; }
    /// <summary>Monday of the ISO week the event was accepted. Never finer.</summary>
    public required DateOnly ReceivedWeek { get; init; }
}
