using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Records;
using Microsoft.Extensions.Logging;

namespace ExitInterviewAgent.Agent.Runner;

/// <summary>What the interviewee sees: the interviewer's words plus the kind and topic as a cue for simulators.</summary>
public sealed record IntervieweeTurn(TurnKind Kind, Topic? Topic, string Text);

/// <summary>
/// The other side of the conversation: a person at a terminal later, a persona simulator today. <c>ReplyAsync</c>
/// returns the raw reply, or <c>null</c> when the interviewee has gone (input closed, connection dropped).
/// <c>DeliverAsync</c> shows the final turn (close or stop), which expects no reply.
/// </summary>
public interface IInterviewee
{
    Task<string?> ReplyAsync(IntervieweeTurn turn, CancellationToken ct);

    Task DeliverAsync(IntervieweeTurn turn, CancellationToken ct);
}

public sealed record InterviewOptions(string EmployerRef, RecordContext Context)
{
    /// <summary>Employer and product names the PII guard must not mistake for people.</summary>
    public IReadOnlyCollection<string> EmployerNames { get; init; } = [];

    public InterviewProtocol Protocol { get; init; } = InterviewProtocol.Current;

    /// <summary>Production: <see cref="InterviewId.NewRandom"/>. Demos and tests inject a seed-derived id so that output is reproducible.</summary>
    public Func<InterviewId> IdFactory { get; init; } = InterviewId.NewRandom;

    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>Receives ids, counts and enum names only (a test asserts that no interview text reaches it).</summary>
    public ILogger? Logger { get; init; }
}

public enum InterviewOutcome
{
    /// <summary>The dialogue ended with consent intact and a record was prepared (it may carry no covered topic).</summary>
    Completed,
    /// <summary>Consent was withdrawn mid-interview: transcript discarded, no record.</summary>
    Withdrawn,
    /// <summary>Consent was refused or never clearly given: nothing kept, no record.</summary>
    ConsentNotGiven,
    /// <summary>The interviewee left without a word: nothing kept, no record.</summary>
    Abandoned,
    /// <summary>The PII detector failed (exception or timeout): nothing may be submitted.</summary>
    PiiGuardFailed,
    /// <summary>The extractor did not produce valid output after one retry: no record.</summary>
    ExtractionFailed,
}

/// <summary>Counts only. Safe to log, trace and print.</summary>
public sealed record RunDiagnostics(
    int InterviewerTurns,
    int IntervieweeTurns,
    int ModelCalls,
    long TokensEstimated,
    int Probes,
    int Clarifications,
    int Redirects,
    int QuestionsRejected,
    int NamesMasked,
    int InjectionSuspectedTurns,
    int ExtractionAttempts,
    int QuotesChecked,
    int QuotesDropped,
    int TopicsForcedToNoData,
    int TopicsCovered,
    bool AiDisclosed);

public sealed record InterviewResult(
    InterviewOutcome Outcome,
    string EndReason,
    Transcript? Transcript,
    InterviewRecord? Record,
    string? RecordJson,
    ValidationOutcome? Validation,
    RunDiagnostics Diagnostics)
{
    /// <summary>At least one topic carries a rating-or-null with quotes. An all-"no data" record says nothing and is not submitted.</summary>
    /// <summary>Controlled codes (never text) of the last rejected extractor output; empty unless the extraction failed.</summary>
    public IReadOnlyList<string> ExtractionErrors { get; init; } = [];

    public bool HasContent => Record is not null && Record.Topics.Enumerate().Any(t => t.Entry.Status == TopicStatus.Covered);

    /// <summary>
    /// The record passed <see cref="RecordValidator"/> and says something. Submission itself is a later task; this only
    /// states that nothing in the agent stands in the way.
    /// </summary>
    public bool Submittable => Outcome == InterviewOutcome.Completed && Validation is { IsValid: true } && HasContent;
}
