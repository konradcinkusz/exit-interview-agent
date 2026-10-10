using System.Text.Json;

namespace ExitInterviewAgent.Contracts;

// Web interview session API (web-app-plan §10). The BFF mirrors these shapes in web/app/lib/interview-contract.ts; a change here
// changes the plan and that file in the same pull request. Records carry no interview text in any log: these are response
// shapes, and only the turn and the reply carry text, by design.

/// <summary>Stable error codes of the session API. The code, not the title, is what a client matches on.</summary>
public static class InterviewCodes
{
    public const string PaymentRequired = "payment_required";
    public const string InterviewsDisabled = "interviews_disabled";
    public const string InterviewInProgress = "interview_in_progress";
    public const string InterviewEnded = "interview_ended";
    public const string ReplyInProgress = "reply_in_progress";
    public const string ReplyInvalid = "reply_invalid";
    public const string InvalidRequest = "invalid_request";
    public const string ProviderUnavailable = "provider_unavailable";
    public const string Gone = "gone";
    public const string NotCompleted = "not_completed";
    public const string NotFound = "not_found";
    public const string RequestCancelled = "request_cancelled";
}

/// <summary><c>POST /api/v1/interviews</c>. <c>Language</c> is <c>pl</c> or <c>en</c>; <c>Tenure</c> is one of the six bands.</summary>
public sealed record CreateInterviewRequest(string? Language, string? Tenure);

/// <summary><c>POST /api/v1/interviews/{id}/reply</c>. One to 2000 characters.</summary>
public sealed record InterviewReplyRequest(string? Text);

/// <summary>One interviewer turn. <c>Kind</c> is one of the nine turn kinds, in snake case; <c>Index</c> counts from 0.</summary>
public sealed record InterviewTurn(int Index, string Kind, string Text);

/// <summary>Set on the last turn only: the reason is a controlled code, never text.</summary>
public sealed record InterviewEnding(string Reason);

/// <summary>Response to <c>POST /api/v1/interviews</c>: the session and the opening turn.</summary>
public sealed record InterviewStarted(string Id, string Status, string Language, DateTimeOffset ExpiresAt, InterviewTurn Turn);

/// <summary>Response to <c>POST /api/v1/interviews/{id}/reply</c>: the next turn, or the end.</summary>
public sealed record InterviewReplyResponse(string Status, InterviewTurn? Turn, InterviewEnding? Ending);

/// <summary>Response to <c>GET /api/v1/interviews/{id}</c>. No text.</summary>
public sealed record InterviewState(string Id, string Status, string Language, int TurnCount, DateTimeOffset ExpiresAt);

/// <summary>One draft text. <c>Kind</c> is <c>facts</c>, <c>overview</c>, <c>what_worked</c>, <c>what_could_improve</c>, <c>for_the_next_person</c>, <c>short_note</c>, <c>glassdoor</c>, <c>google_review</c> or <c>reddit</c>.</summary>
public sealed record InterviewTile(string Kind, string Text);

/// <summary>A tile that did not pass the checks: the code only.</summary>
public sealed record InterviewDroppedTile(string Code);

/// <summary>The tiles and the legal notice the CLI prints, in the interview's language.</summary>
public sealed record InterviewTiles(IReadOnlyList<InterviewTile> Items, IReadOnlyList<InterviewDroppedTile> Dropped, string Notice);

/// <summary>Counts only.</summary>
public sealed record InterviewUsage(int ModelCalls, long TokensEstimated);

/// <summary>Response to <c>GET /api/v1/interviews/{id}/result</c>, once the status is <c>completed</c>. <c>Record</c> is the validated record JSON.</summary>
public sealed record InterviewResultResponse(JsonElement Record, InterviewTiles Tiles, InterviewUsage Usage);
