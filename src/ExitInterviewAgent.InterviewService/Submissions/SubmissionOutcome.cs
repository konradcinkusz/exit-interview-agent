using ExitInterviewAgent.Contracts;

namespace ExitInterviewAgent.InterviewService.Submissions;

/// <summary>A stable rejection: a code, validation errors (code and schema path) and PII kinds. Never submitted text.</summary>
public sealed record Rejection(string Code, IReadOnlyList<FieldError> Errors, IReadOnlyList<string> PiiKinds)
{
    public static Rejection Of(string code) => new(code, [], []);
}

/// <summary>
/// The result of a submission: a receipt code (shown once, never retrievable again) or a rejection.
/// The one type the web endpoint, the MCP tool and the CLI endpoint all return.
/// </summary>
public sealed record SubmissionOutcome(string? ReceiptCode, Rejection? Rejection)
{
    public bool Accepted => ReceiptCode is not null;
    public static SubmissionOutcome Accept(string receiptCode) => new(receiptCode, null);
    public static SubmissionOutcome Reject(string code) => new(null, Rejection.Of(code));
    public static SubmissionOutcome Reject(Rejection rejection) => new(null, rejection);
}
