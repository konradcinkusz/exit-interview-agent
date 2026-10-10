using System.Diagnostics;
using System.Text;
using ExitInterviewAgent.Agent.Machine;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Roles;
using ExitInterviewAgent.Agent.Tracing;
using ExitInterviewAgent.Records;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Attr = ExitInterviewAgent.Agent.Tracing.InterviewTelemetry.Attr;
using Ev = ExitInterviewAgent.Agent.Tracing.InterviewTelemetry.Events;

namespace ExitInterviewAgent.Agent.Runner;

/// <summary>
/// Runs one interview: drives <see cref="InterviewMachine"/>, words questions through the roles, masks every reply on
/// arrival, and, when the dialogue closes with consent intact, extracts, verifies, builds and validates the record.
/// The runner owns no policy of its own: what to ask next is the machine's, how to word it is a role's (checked by
/// <see cref="QuestionGuard"/>), what may be kept is the PII guard's and the quote step's.
/// </summary>
public sealed class InterviewRunner
{
    private readonly IInterviewer _interviewer;
    private readonly IProber _prober;
    private readonly IRecordExtractor _extractor;
    private readonly IPiiGuard _pii;
    private readonly ModelMeter _meter;
    private readonly InterviewOptions _options;
    private readonly InterviewProtocol _protocol;
    private readonly RecordValidator _validator = new();

    public InterviewRunner(IInterviewer interviewer, IProber prober, IRecordExtractor extractor, IPiiGuard pii, ModelMeter meter, InterviewOptions options)
    {
        if (!EmployerRef.IsValid(options.EmployerRef)) throw new ArgumentException("Employer reference is not valid.", nameof(options));
        _interviewer = interviewer;
        _prober = prober;
        _extractor = extractor;
        _pii = pii;
        _meter = meter;
        _options = options;
        _protocol = options.Protocol;
    }

    /// <summary>
    /// Builds the standard runner: model roles on a metered <paramref name="model"/> and a fail-closed PII guard. <paramref name="meter"/> is the
    /// interview's budget; a caller that makes further model calls for the same interview (the tiles, Y4) passes the same one, so they count.
    /// </summary>
    public static InterviewRunner Create(IChatClient model, InterviewOptions options, ModelMeter? meter = null)
    {
        meter ??= new ModelMeter(options.Protocol.Limits);
        IChatClient metered = new MeteredChatClient(model, meter);
        return new InterviewRunner(
            new ModelInterviewer(metered, options.Protocol), new ModelProber(metered, options.Protocol), new ModelRecordExtractor(metered, options.Protocol),
            new PiiGuard(options.EmployerNames), meter, options);
    }

    public async Task<InterviewResult> RunAsync(IInterviewee interviewee, CancellationToken ct = default)
    {
        var machine = new InterviewMachine(_protocol);
        var transcript = new Transcript();
        var counters = new Counters();
        var polarity = new Dictionary<Topic, int>();
        var contradicted = new HashSet<Topic>();
        var aiDisclosed = false;
        var started = _options.Clock.GetTimestamp();

        // The wording in use. It starts as the interview's own protocol and moves when the interviewee writes in, or asks for, the other
        // language (Y4). The machine, its topics and its limits are not touched by that: only the words of the next question change.
        var wording = _protocol;
        var turnLanguages = new List<string>();
        string? confirmation = null;

        using var session = InterviewTelemetry.Source.StartActivity(InterviewTelemetry.Spans.Session, ActivityKind.Internal);
        session?.SetTag(Attr.OperationName, "invoke_agent");
        session?.SetTag(Attr.AgentName, "exit-interview-agent");
        session?.SetTag(Attr.ProtocolVersion, SpanTags.SafeCode(_protocol.ProtocolVersion));

        var step = machine.Start();
        while (true)
        {
            using var turn = InterviewTelemetry.Source.StartActivity(InterviewTelemetry.Spans.Turn, ActivityKind.Internal);
            turn.Set(Attr.TurnIndex, machine.InterviewerTurns).Set(Attr.TurnKind, step.Kind).Set(Attr.Phase, machine.Phase);
            if (step.Topic is { } stepTopic) turn.Set(Attr.Topic, stepTopic);

            var text = await WordAsync(step, transcript, counters, wording, confirmation, ct).ConfigureAwait(false);
            if (IsQuestion(step.Kind)) confirmation = null;
            var outgoing = new IntervieweeTurn(step.Kind, step.Topic, text);

            if (step.Kind is TurnKind.Close or TurnKind.Stop)
            {
                if (step.Stop != StopReason.Disconnected) await interviewee.DeliverAsync(outgoing, ct).ConfigureAwait(false);
                if (step.Kind == TurnKind.Close) transcript.Add(Speaker.Interviewer, step.Kind, null, text);
                turn.Set(Attr.Decision, step.Kind);
                break;
            }

            var topic = machine.CurrentTopic;
            transcript.Add(Speaker.Interviewer, step.Kind, topic, text);
            var raw = await interviewee.ReplyAsync(outgoing, ct).ConfigureAwait(false);
            if (step.Kind == TurnKind.Opening)
            {
                // Set only now that the disclosure turn has actually been delivered and answered.
                aiDisclosed = raw is not null;
                if (aiDisclosed) session.Event(Ev.DisclosureDelivered);
            }
            if (raw is null)
            {
                step = machine.OnDisconnect();
                turn.Set(Attr.Decision, step.Kind);
                continue;
            }

            counters.IntervieweeTurns++;
            var masked = MaskReply(raw, step.Kind, topic, transcript, counters, turn, out var guardOk, out var namesPerson);
            if (!guardOk)
            {
                counters.InterviewerTurns = machine.InterviewerTurns;
                return Discard(InterviewOutcome.PiiGuardFailed, "pii_guard_failed", counters, session, aiDisclosed);
            }

            // Y4: the language of this reply counts for the record, and an explicit request or a clear reply moves the next question. The
            // disclosure and consent replies are not part of the dialogue yet, so they are left out.
            if (step.Kind is not (TurnKind.Opening or TurnKind.ConsentReask))
            {
                if (InterviewLanguage.TurnLanguage(masked) is { } turnLanguage) turnLanguages.Add(turnLanguage);
                if (InterviewLanguage.SwitchTo(masked, wording.Language) is { } next)
                {
                    wording = InterviewProtocol.For(next).WithLimits(_protocol.Limits);
                    confirmation = InterviewLanguage.Confirmation(next);
                }
            }

            polarity.TryGetValue(topic ?? default, out var previous);
            var signals = ReplyAnalyzer.Analyze(masked, _protocol.Limits, namesPerson, topic is null ? 0 : previous);
            if (topic is { } t && signals.Polarity != 0) polarity[t] = signals.Polarity;
            if (signals.Contradiction && topic is { } ct2) contradicted.Add(ct2);
            if (signals.InjectionSuspected) { counters.InjectionTurns++; turn.Event(Ev.InjectionSuspected); }
            Trace(turn, signals, raw.Length, masked);

            var budgetGone = _meter.Exhausted;
            step = machine.OnReply(signals, budgetGone);
            if (budgetGone) session.Event(Ev.BudgetExhausted, (Attr.ModelCalls, _meter.Calls));
            turn.Set(Attr.Decision, step.Kind);
            _options.Logger?.LogDebug("Turn {Index}: {Kind} -> {Next}", machine.InterviewerTurns, signals.Withdrawal ? "withdrawal" : "reply", step.Kind);
        }

        counters.InterviewerTurns = machine.InterviewerTurns;
        if (step.Kind == TurnKind.Stop)
        {
            // Consent withdrawn, refused or never given (or nobody there): the transcript is discarded, no record exists.
            var (outcome, reason) = step.Stop switch
            {
                StopReason.ConsentWithdrawn => (InterviewOutcome.Withdrawn, "consent_withdrawn"),
                StopReason.ConsentDeclined => (InterviewOutcome.ConsentNotGiven, "consent_declined"),
                StopReason.ConsentUnclear => (InterviewOutcome.ConsentNotGiven, "consent_unclear"),
                _ => (InterviewOutcome.Abandoned, "disconnected"),
            };
            session.Event(Ev.ConsentWithdrawn);
            session.Event(Ev.TranscriptDiscarded);
            return Discard(outcome, reason, counters, session, aiDisclosed);
        }

        session.Event(Ev.ConsentGranted);
        return await FinishAsync(transcript, contradicted, step.Close!.Value, counters, aiDisclosed, started, turnLanguages, session, ct).ConfigureAwait(false);
    }

    // ---- wording -----------------------------------------------------------------------------------------------

    private static bool IsQuestion(TurnKind kind) => kind is TurnKind.Topic or TurnKind.Probe or TurnKind.Clarification or TurnKind.DeepProbe or TurnKind.Redirect;

    /// <summary>
    /// Words the next turn. <paramref name="wording"/> is the protocol the interview is in now; <paramref name="confirmation"/> is the one
    /// sentence said before a question after a language switch (it is put before the question, and only a question takes it).
    /// </summary>
    private async Task<string> WordAsync(Step step, Transcript history, Counters c, InterviewProtocol wording, string? confirmation, CancellationToken ct)
    {
        switch (step.Kind)
        {
            case TurnKind.Opening: return _protocol.Opening;
            case TurnKind.ConsentReask: return _protocol.ConsentReask;
            case TurnKind.Close: return wording.Closings[step.Close!.Value];
            case TurnKind.Stop:
                return step.Stop switch { StopReason.ConsentWithdrawn => wording.AckWithdrawn, _ => wording.AckDeclined };
        }

        var seed = step.Kind switch
        {
            TurnKind.Topic => wording.Spec(step.Topic!.Value).Question,
            TurnKind.Probe => wording.Probe,
            TurnKind.Clarification => wording.Clarification,
            TurnKind.DeepProbe => wording.DeepeningSeeds[(int)step.Focus!.Value],
            _ => wording.RedirectNames,
        };
        var request = new QuestionRequest(step.Kind, step.Topic, seed, history, step.Focus, wording);
        var prober = step.Kind is TurnKind.Probe or TurnKind.DeepProbe;
        string? proposed;
        using (var span = prober ? InterviewTelemetry.Source.StartActivity(InterviewTelemetry.Spans.Probe) : null)
        {
            span?.Set(Attr.Role, Role.Prober);
            try
            {
                proposed = prober
                    ? await _prober.ProbeAsync(request, ct).ConfigureAwait(false)
                    : await _interviewer.AskAsync(request, ct).ConfigureAwait(false);
            }
            catch (ModelCallFailedException e) when (!e.IsFatal)
            {
                proposed = null;
            }
        }

        var verdict = proposed is null ? new GuardVerdict(false, "model_error") : QuestionGuard.Check(proposed, step.Kind, _pii);
        var text = seed;
        if (verdict.Ok) text = proposed!.Trim();
        else
        {
            c.QuestionsRejected++;
            Activity.Current.Event(Ev.QuestionRejected, ("reason", RejectionCode(verdict.Reason)));
        }

        switch (step.Kind)
        {
            case TurnKind.Probe: c.Probes++; break;
            case TurnKind.Clarification: c.Clarifications++; break;
            case TurnKind.Redirect: c.Redirects++; break;
        }

        var prefaced = step.Preface switch
        {
            Preface.AcknowledgeFrustration => $"{wording.AckFrustration} {text}",
            Preface.DeepeningReminder => $"{wording.DeepeningReminder} {text}",
            _ => text,
        };
        return confirmation is null ? prefaced : $"{confirmation} {prefaced}";
    }

    /// <summary>Index into <see cref="Reasons"/>, so the trace carries a number and never a string built from content.</summary>
    private static int RejectionCode(string reason) => Array.IndexOf(Reasons, reason) + 1;

    private static readonly string[] Reasons = ["empty", "too_long", "multi_paragraph", "prompt_leak", "no_question", "multiple_questions", "leading", "loaded", "closed_question", "probe_without_example", "pii", "model_error", "double_barrelled"];

    // ---- replies -----------------------------------------------------------------------------------------------

    private string MaskReply(string raw, TurnKind answering, Topic? topic, Transcript transcript, Counters c, Activity? turn, out bool ok, out bool namesPerson)
    {
        var cleaned = ReplySanitizer.Clean(raw, _protocol.Limits.MaxReplyChars);
        using var span = InterviewTelemetry.Source.StartActivity(InterviewTelemetry.Spans.PiiGuard);
        var result = _pii.Mask(cleaned);
        ok = result.Ok;
        namesPerson = result.NamesPerson;
        span.Set(Attr.PiiFailClosed, !result.Ok);
        span.Set(Attr.PiiFindings, result.Findings.Count).Set(Attr.PiiKinds, result.Findings.Select(f => f.Kind));
        if (!ok) return string.Empty;
        if (namesPerson) { c.NamesMasked++; turn.Event(Ev.NamesMasked, (Attr.PiiFindings, result.Findings.Count(f => f.Kind == Privacy.PiiKind.PersonName))); }
        transcript.Add(Speaker.Interviewee, answering, topic, result.MaskedText);
        return result.MaskedText;
    }

    private static void Trace(Activity? turn, ReplySignals s, int rawChars, string masked)
    {
        turn.Set(Attr.ReplyChars, rawChars).Set(Attr.ReplyWords, s.Words);
        turn.Set(Attr.SignalVague, s.Vague).Set(Attr.SignalTerse, s.Terse).Set(Attr.SignalHostile, s.Hostile);
        turn.Set(Attr.SignalContradiction, s.Contradiction).Set(Attr.SignalWithdrawal, s.Withdrawal);
        turn.Set(Attr.SignalNames, s.NamesPerson).Set(Attr.SignalInjection, s.InjectionSuspected).Set(Attr.SignalSerious, s.Serious);
    }

    // ---- ending ------------------------------------------------------------------------------------------------

    private InterviewResult Discard(InterviewOutcome outcome, string reason, Counters c, Activity? session, bool aiDisclosed)
    {
        session?.SetTag(Attr.Outcome, SpanTags.Snake(outcome.ToString()));
        session?.SetTag(Attr.EndReason, reason);
        session.Set(Attr.Submittable, false);
        return new InterviewResult(outcome, reason, null, null, null, null, Diagnostics(c, aiDisclosed));
    }

    private async Task<InterviewResult> FinishAsync(Transcript transcript, HashSet<Topic> contradicted, CloseReason close, Counters c, bool aiDisclosed, long started, IReadOnlyList<string> turnLanguages, Activity? session, CancellationToken ct)
    {
        var endReason = SpanTags.Snake(close.ToString());
        ExtractorOutput? extracted = null;
        IReadOnlyList<string> errors = [];
        for (var attempt = 1; attempt <= 2 && extracted is null; attempt++)
        {
            c.ExtractionAttempts = attempt;
            using var span = InterviewTelemetry.Source.StartActivity(InterviewTelemetry.Spans.Extraction);
            span.Set(Attr.Attempts, attempt);
            string text;
            try { text = await _extractor.ExtractAsync(transcript, errors, ct).ConfigureAwait(false); }
            catch (ModelCallFailedException e) when (!e.IsFatal)
            {
                errors = ["extractor.model_error"];
                span.Set(Attr.SchemaValid, false);
                continue;
            }
            var ok = ExtractorOutput.TryParse(text, out extracted, out var codes);
            errors = codes;
            span.Set(Attr.SchemaValid, ok).SetCodes(Attr.ErrorCodes, codes);
        }

        if (extracted is null)
        {
            session?.SetTag(Attr.Outcome, "extraction_failed");
            session.Set(Attr.Submittable, false);
            return new InterviewResult(InterviewOutcome.ExtractionFailed, "extraction_invalid", transcript, null, null, null, Diagnostics(c, aiDisclosed));
        }

        var elapsed = _options.Clock.GetElapsedTime(started);
        var recordLanguage = InterviewLanguage.RecordLanguage(turnLanguages, _protocol.Language);
        var metadata = new InterviewMetadata(_protocol.ProtocolVersion, recordLanguage, aiDisclosed, DurationBandOf(elapsed), TurnBandOf(transcript.IntervieweeTurns));

        InterviewRecord record;
        AssemblyReport report;
        using (var span = InterviewTelemetry.Source.StartActivity(InterviewTelemetry.Spans.QuoteVerification))
        {
            (record, report) = RecordAssembler.Assemble(extracted, transcript, _protocol, _pii, _options.EmployerRef, _options.Context, metadata, _options.IdFactory(), contradicted);
            c.QuotesChecked = report.QuotesChecked;
            c.QuotesDropped = report.QuotesDropped;
            c.Forced = report.TopicsForcedToNoData;
            c.Covered = report.TopicsCovered;
            span.Set(Attr.QuotesChecked, report.QuotesChecked).Set(Attr.QuotesDropped, report.QuotesDropped).Set(Attr.TopicsCovered, report.TopicsCovered);
            if (report.QuotesDropped > 0) span.Event(Ev.QuotesDropped, (Attr.QuotesDropped, report.QuotesDropped));
        }

        ValidationOutcome validation;
        string json;
        using (var span = InterviewTelemetry.Source.StartActivity(InterviewTelemetry.Spans.Validation))
        {
            json = RecordSerializer.SerializeCanonicalString(record);
            validation = _validator.Validate(json);
            span.Set(Attr.RecordValid, validation.IsValid).SetCodes(Attr.ErrorCodes, validation.Errors.Select(e => e.Code));
        }

        var result = new InterviewResult(InterviewOutcome.Completed, endReason, transcript, record, json, validation, Diagnostics(c, aiDisclosed));
        session?.SetTag(Attr.Outcome, "completed");
        session?.SetTag(Attr.EndReason, endReason);
        session.Set(Attr.Turns, transcript.Turns.Count).Set(Attr.ModelCalls, _meter.Calls).Set(Attr.TopicsCovered, report.TopicsCovered).Set(Attr.AiDisclosed, aiDisclosed).Set(Attr.Submittable, result.Submittable);
        session?.SetTag(Attr.TokensEstimated, _meter.Tokens);
        return result;
    }

    private RunDiagnostics Diagnostics(Counters c, bool aiDisclosed) => new(
        c.InterviewerTurns, c.IntervieweeTurns, _meter.Calls, _meter.Tokens,
        c.Probes, c.Clarifications, c.Redirects, c.QuestionsRejected, c.NamesMasked, c.InjectionTurns, c.ExtractionAttempts,
        c.QuotesChecked, c.QuotesDropped, c.Forced, c.Covered, aiDisclosed);

    public static DurationBand DurationBandOf(TimeSpan d) =>
        d < TimeSpan.FromMinutes(10) ? DurationBand.LessThan10Minutes : d < TimeSpan.FromMinutes(20) ? DurationBand.TenToTwentyMinutes : d < TimeSpan.FromMinutes(40) ? DurationBand.TwentyToFortyMinutes : DurationBand.OverFortyMinutes;

    public static TurnBand TurnBandOf(int intervieweeTurns) =>
        intervieweeTurns < 10 ? TurnBand.LessThan10 : intervieweeTurns < 20 ? TurnBand.TenToTwenty : intervieweeTurns < 40 ? TurnBand.TwentyToForty : TurnBand.OverForty;

    private sealed class Counters
    {
        public int InterviewerTurns, IntervieweeTurns, Probes, Clarifications, Redirects, QuestionsRejected, NamesMasked, InjectionTurns, ExtractionAttempts, QuotesChecked, QuotesDropped, Forced, Covered;
    }
}

/// <summary>Removes control and bidirectional-override characters, collapses white space, and bounds the length.</summary>
public static class ReplySanitizer
{
    /// <summary>U+2028, U+2029, U+202A..U+202E and U+2066..U+2069: separators and bidirectional overrides the record schema rejects.</summary>
    private static bool IsFormatOverride(char ch) => ch is (char)0x2028 or (char)0x2029 || ch >= (char)0x202A && ch <= (char)0x202E || ch >= (char)0x2066 && ch <= (char)0x2069;

    public static string Clean(string raw, int maxChars)
    {
        var sb = new StringBuilder(Math.Min(raw.Length, maxChars + 2));
        foreach (var ch in raw)
        {
            if (char.IsWhiteSpace(ch)) { sb.Append(' '); continue; }
            if (char.IsControl(ch) || IsFormatOverride(ch)) continue;
            sb.Append(ch);
        }
        var text = QuoteVerifier.Normalize(sb.ToString());
        if (text.Length <= maxChars) return text;
        var cut = maxChars;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return QuoteVerifier.Normalize(text[..cut]);
    }
}
