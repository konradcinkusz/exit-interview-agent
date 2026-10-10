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
