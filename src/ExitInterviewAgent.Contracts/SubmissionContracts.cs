namespace ExitInterviewAgent.Contracts;

/// <summary>
/// Stable machine-readable rejection codes of the submission, ticket and receipt endpoints. Part of the public contract:
/// codes are never renamed or reused. Record-validation codes (<c>MISSING_FIELD</c>, <c>LENGTH_LIMIT</c>, ...) are the
/// ones published by the record library and are passed through unchanged. No code, and no message, ever carries
/// submitted text.
/// </summary>
public static class SubmissionCodes
{
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
    public const string AiNotDisclosed = "AI_NOT_DISCLOSED";
    /// <summary>The server-side re-scan found personal data in a quote or field. The response names kinds only.</summary>
    public const string PiiDetected = "PII_DETECTED";
    /// <summary>The re-scan could not complete (error or timeout). Fail closed: nothing is stored.</summary>
    public const string PiiCheckFailed = "PII_CHECK_FAILED";
    /// <summary>This account already submitted for this employer inside the ledger window.</summary>
    public const string AlreadySubmitted = "ALREADY_SUBMITTED";
    public const string EmploymentNotVerified = "EMPLOYMENT_NOT_VERIFIED";
    /// <summary>Unknown, expired and already-used tickets are indistinguishable.</summary>
    public const string TicketInvalid = "TICKET_INVALID";
    public const string TicketLimit = "TICKET_LIMIT";
    /// <summary>The receipt code is not well formed (wrong length, alphabet or checksum). Says nothing about any record.</summary>
    public const string InvalidReceiptCode = "INVALID_RECEIPT_CODE";
    public const string RateLimited = "RATE_LIMITED";
}

/// <summary>Request headers that carry bearer secrets. Secrets travel in headers, never in a URL or a query string.</summary>
public static class SubmissionHeaders
{
    public const string Ticket = "X-Submission-Ticket";
    public const string ReceiptCode = "X-Receipt-Code";
}

/// <summary>One validation failure: a stable code and a path made of schema-defined names only.</summary>
public sealed record FieldError(string Code, string Path);

/// <summary>
/// Success body of a submission. The receipt code is a bearer secret shown exactly once: the server stores only its hash,
/// so a lost code cannot be recovered and anyone holding it can delete the record.
/// </summary>
public sealed record SubmissionAccepted(string ReceiptCode);

/// <summary>A submission ticket for the CLI: random, single use, not bound to an employer. Shown once.</summary>
public sealed record TicketIssued(string Ticket, DateTimeOffset ExpiresAt);
