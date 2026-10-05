namespace ExitInterviewAgent.InterviewService.Persistence;

/// <summary>
/// How far the employer claim was checked at submission time. Kept in the store row and deliberately NOT in the record
/// schema (the record carries nothing about the person; this is a property of the store's check, not of the interview).
/// </summary>
public enum VerificationLevel
{
    /// <summary>The verifier could not be consulted (degraded, P8). The record is stored all the same.</summary>
    Unchecked = 0,

    /// <summary>The verifier was consulted and could not confirm the claim.</summary>
    Unverified = 1,

    /// <summary>The verifier confirmed the claim (the mock does so only when configured to).</summary>
    Verified = 2,
}

/// <summary>
/// A stored record. The key is the pseudonymous interview id from the record itself. There is no account column, no
/// precise timestamp and no receipt column here: <see cref="CreatedWeek"/> is a coarse bucket used only to purge by age.
/// </summary>
public sealed class RecordRow
{
    public required string Id { get; init; }
    public required string EmployerRef { get; init; }
    public required string Json { get; init; }
    public required VerificationLevel Verification { get; init; }
    /// <summary>Monday of the ISO week the row was written (ADR-0027). Never finer.</summary>
    public required DateOnly CreatedWeek { get; init; }
}

/// <summary>
/// One submission per employer per account. A keyed HMAC tag and its key id, a random row key and a week bucket.
/// No record id, no interview id, no receipt reference, no employer in the clear (ADR-0028).
/// </summary>
public sealed class LedgerEntry
{
    public required Guid Id { get; init; }
    public required string KeyId { get; init; }
    public required string Tag { get; init; }
    public required DateOnly CreatedWeek { get; init; }
}

/// <summary>The hash of a receipt code and the record it deletes. No account link, by design.</summary>
public sealed class ReceiptRow
{
    public required Guid Id { get; init; }
    public required string CodeHash { get; init; }
    public required string RecordId { get; init; }
}

/// <summary>
/// The one table that holds an account subject: a short-lived, single-use CLI ticket (brief section 4). It carries no
/// employer and no record reference, and the row is deleted when the ticket is redeemed or expires.
/// </summary>
public sealed class TicketRow
{
    public required Guid Id { get; init; }
    public required string TokenHash { get; init; }
    public required string Sub { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}
