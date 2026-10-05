namespace ExitInterviewAgent.InterviewService.Submissions;

/// <summary>
/// Everything operator-tunable about submission, bound from configuration. No default is a secret; the ledger keys
/// (<see cref="LedgerOptions"/>) have no default at all outside Development.
/// </summary>
public sealed class SubmissionOptions
{
    public const string SectionName = "Submission";

    /// <summary>Server-side PII re-scan. <see cref="PiiOptions.AllowList"/> names employer and product words that are not people.</summary>
    public PiiSettings Pii { get; set; } = new();
    public VerificationSettings Verification { get; set; } = new();
    public TicketSettings Tickets { get; set; } = new();
    public ReceiptSettings Receipts { get; set; } = new();
    public AnonymousLimitSettings Limits { get; set; } = new();
    public RetentionSettings Retention { get; set; } = new();

    /// <summary>
    /// Header that carries the real client address when (and only when) a trusted proxy sits in front, for example
    /// <c>Fly-Client-IP</c>. Unset: the socket address is used. Never trusted by default (SERVICE-API-PATTERNS section 1).
    /// </summary>
    public string? ClientIpHeader { get; set; }
}

public sealed class PiiSettings
{
    public bool FailClosed { get; set; }
    public string[] AllowList { get; set; } = [];
    /// <summary>Upper bound for the whole re-scan of one record; exceeding it rejects the submission.</summary>
    public int TimeoutMilliseconds { get; set; } = 5000;
}

public sealed class VerificationSettings
{
    /// <summary><c>verified</c> | <c>unverified</c> | <c>unavailable</c>: what the MOCK verifier answers. There is no real verifier (OP-1).</summary>
    public string MockMode { get; set; } = "unverified";
    /// <summary>Stricter policy: reject a claim the verifier could not confirm. Off by default: with no real verifier nothing could ever be accepted.</summary>
    public bool RejectUnverified { get; set; }
    public int TimeoutMilliseconds { get; set; } = 2000;
}

public sealed class TicketSettings
{
    public int TtlMinutes { get; set; } = 15;
    /// <summary>Expiry is rounded UP to this many minutes so the row does not record the mint instant to the second (ADR-0030).</summary>
    public int ExpiryGranularityMinutes { get; set; } = 5;
    public int MaxOutstandingPerAccount { get; set; } = 3;
    public int MintsPerAccountPerHour { get; set; } = 10;
}

public sealed class ReceiptSettings
{
    /// <summary>Every deletion request takes at least this long, so a hit and a miss cannot be told apart by latency (ADR-0029).</summary>
    public int ResponseFloorMilliseconds { get; set; } = 150;
}

public sealed class AnonymousLimitSettings
{
    public int ReceiptDeletePerIpPerMinute { get; set; } = 6;
    public int ReceiptDeleteGlobalPerMinute { get; set; } = 60;
    public int TicketedSubmitPerIpPerMinute { get; set; } = 6;
    public int TicketedSubmitGlobalPerMinute { get; set; } = 60;
}

public sealed class RetentionSettings
{
    /// <summary>Maximum record age in months. ADR-0019: 24, an assumption and not a legal determination.</summary>
    public int RecordMaxAgeMonths { get; set; } = 24;
    /// <summary>Ledger window in days. ADR-0028: 365.</summary>
    public int LedgerWindowDays { get; set; } = 365;
    public int SweepIntervalMinutes { get; set; } = 5;
}

public sealed class LedgerOptions
{
    public const string SectionName = "Ledger";

    /// <summary>Id of the key that signs new entries. Every key in <see cref="Keys"/> is tried on lookup.</summary>
    public string? ActiveKeyId { get; set; }
    public List<LedgerKeyOptions> Keys { get; set; } = [];
}

public sealed class LedgerKeyOptions
{
    public string? Id { get; set; }
    /// <summary>Base64, at least 32 bytes decoded. Comes from the environment or a secret store, never from source.</summary>
    public string? Secret { get; set; }
}
