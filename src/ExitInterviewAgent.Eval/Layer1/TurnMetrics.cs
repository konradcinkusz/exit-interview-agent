using ExitInterviewAgent.Agent.Machine;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Runner;
using ExitInterviewAgent.Agent.Tracing;
using ExitInterviewAgent.Eval.Execution;
using ExitInterviewAgent.Eval.Stats;

namespace ExitInterviewAgent.Eval.Layer1;

/// <summary>
/// The trace codes of the interview's moves (<c>interview.turn.move</c>, spec §5.2): the snake case of the agent's <c>Move</c> enum, a
/// controlled vocabulary like <c>interview.turn.kind</c>. A test ties <see cref="All"/> to the enum, so a new move cannot go unmeasured.
/// </summary>
public static class MoveCodes
{
    public const string Warmup = "warmup";
    public const string Open = "open";
    public const string Scale = "scale";
    public const string ScaleFollowUp = "scale_follow_up";
    public const string Example = "example";
    public const string Elaborate = "elaborate";
    public const string Ladder = "ladder";
    public const string Consequence = "consequence";
    public const string Retention = "retention";
    public const string AnythingElse = "anything_else";
    public const string Deep = "deep";
    public const string Clarify = "clarify";
    public const string Redirect = "redirect";

    public static IReadOnlyList<string> All { get; } =
        [Warmup, Open, Scale, ScaleFollowUp, Example, Elaborate, Ladder, Consequence, Retention, AnythingElse, Deep, Clarify, Redirect];

    /// <summary>The moves that ask the person for more about the same topic (spec §3.4).</summary>
    public static IReadOnlySet<string> FollowUps { get; } = new HashSet<string>(StringComparer.Ordinal) { ScaleFollowUp, Example, Elaborate, Ladder, Consequence, Deep };

    /// <summary>The code when it is one of the vocabulary, otherwise null (a misspelt code must not be measured as a move).</summary>
    public static string? Parse(string code) => All.Contains(code, StringComparer.Ordinal) ? code : null;
}

/// <summary>
/// One interviewer turn and the reply to it, as the measures read them. Kind, topic, move, preface, the reply's word count, the signals and
/// the retry come from the trace (§5.2). <see cref="Scale"/> is the 1–5 digit of a rating reply: it is not in the trace, so the grader sets it
/// from the transcript (<see cref="TurnFacts.WithRatings"/>) and it is null when that is not known.
/// </summary>
public sealed record TurnFact(string Kind, string? Topic, string? Move, string? Preface, int Words, bool Vague, bool Terse, bool Rejected, bool? Retried, int? Scale = null);

public static class TurnFacts
{
    public const string MoveKey = "interview.turn.move";
    public const string PrefaceKey = "interview.turn.preface";
    public const string RetriedKey = "interview.question.retried";

    public static IReadOnlyList<TurnFact> FromTrace(CapturedTrace trace) =>
        trace.Turns.OrderBy(s => s.StartTicks).Select(s => new TurnFact(
            s.Str(InterviewTelemetry.Attr.TurnKind) ?? "?",
            s.Str(InterviewTelemetry.Attr.Topic),
            s.Str(MoveKey),
            s.Str(PrefaceKey),
            s.Int(InterviewTelemetry.Attr.ReplyWords) ?? 0,
            s.Bool(InterviewTelemetry.Attr.SignalVague) == true,
            s.Bool(InterviewTelemetry.Attr.SignalTerse) == true,
            s.HasEvent(InterviewTelemetry.Events.QuestionRejected),
            s.Bool(RetriedKey))).ToList();

    /// <summary>
    /// Sets the rating digit on each Scale turn, read from the reply to it in the transcript. The trace and the transcript are matched by
    /// order, one interviewer turn each; when their counts differ the alignment is unknown and no rating is set. The digit is read as
    /// <see cref="ReplyAnalyzer.ScaleValueOf"/> reads it: word forms are not read here.
    /// </summary>
    public static IReadOnlyList<TurnFact> WithRatings(IReadOnlyList<TurnFact> facts, Transcript? transcript)
    {
        if (transcript is null) return facts;
        var asked = transcript.Turns.Select((t, i) => (t, i)).Where(x => x.t.Speaker == Speaker.Interviewer).Select(x => x.i).ToList();
        if (asked.Count != facts.Count) return facts;
        return facts.Select((f, k) => f.Move == MoveCodes.Scale ? f with { Scale = RatingAfter(transcript, asked[k]) } : f).ToList();
    }

    private static int? RatingAfter(Transcript transcript, int askedAt)
    {
        var reply = transcript.Turns.Skip(askedAt + 1).FirstOrDefault(t => t.Speaker == Speaker.Interviewee)?.Text;
        return reply is null ? null : ReplyAnalyzer.ScaleValueOf(reply.ToLowerInvariant());
    }
}

/// <summary>
/// Protocol 1.3 turn metrics (spec §6.1, Q-01..Q-04, Q-09, Q-12, Q-13). Each is a pure function of the turn facts, so a crafted trace can
/// show that it fails. A metric with no observations returns <see cref="Count.Zero"/>, which pools to nothing rather than to a pass.
/// </summary>
public static class TurnMetrics
{
    private const string ReasonTopic = "reason_for_leaving";
    private static readonly HashSet<string> QuestionKinds = new(StringComparer.Ordinal) { "topic", "probe", "clarification", "redirect", "deep_probe" };

    /// <summary>Q-04 caps, by the persona's declared behaviour (spec §6.1). Other personas have no cap and are not measured.</summary>
    public static int? QuestionCap(IReadOnlyList<string> behaviours)
    {
        if (behaviours.Contains("talkative")) return 18;
        if (behaviours.Contains("terse")) return 6;
        return null;
    }

    /// <summary>Q-01: the first core topic after the reason is the persona's salient one. Unmeasured without moves or without a salient topic.</summary>
    public static Count OrderFollowsSalience(IReadOnlyList<TurnFact> f, string? salient)
    {
        if (salient is null || NoMoves(f)) return Count.Zero;
        var firstCore = f.FirstOrDefault(x => x.Move == MoveCodes.Open && x.Topic != ReasonTopic)?.Topic;
        return new Count(firstCore == salient ? 1 : 0, 1);
    }

    /// <summary>Q-02: core topics (an open question) with a substantive reply that got at least one follow-up on the same topic.</summary>
    public static Count ProbeOnCore(IReadOnlyList<TurnFact> f, int substantiveWords)
    {
        var probed = 0;
        var substantive = 0;
        for (var i = 0; i < f.Count; i++)
        {
            if (f[i].Move != MoveCodes.Open || f[i].Words < substantiveWords) continue;
            substantive++;
            if (FollowedOnSameTopic(f, i)) probed++;
        }
        return new Count(probed, substantive);
    }

    /// <summary>Q-03: light topics rated 3 or 4 that were not followed up. Ratings 1, 2 and 5 are the scale follow-up case, not measured here.</summary>
    public static Count NoProbeOnLightMid(IReadOnlyList<TurnFact> f)
    {
        var released = 0;
        var mid = 0;
        for (var i = 0; i < f.Count; i++)
        {
            if (f[i].Move != MoveCodes.Scale || f[i].Scale is not (3 or 4)) continue;
            mid++;
            if (!FollowedOnSameTopic(f, i)) released++;
        }
        return new Count(released, mid);
    }

    /// <summary>Q-04: the interview asked no more questions than the cap (one interview, so the pooled rate is the share within the cap).</summary>
    public static Count QuestionsWithinCap(IReadOnlyList<TurnFact> f, int? cap)
    {
        if (cap is not { } limit) return Count.Zero;
        var questions = f.Count(x => QuestionKinds.Contains(x.Kind));
        return new Count(questions <= limit ? 1 : 0, 1);
    }

    /// <summary>Q-09: questions rejected again after the retry, so the fixed protocol text went out. A rejection that was not retried is not counted.</summary>
    public static Count ReplacedRate(IReadOnlyList<TurnFact> f)
    {
        var questions = f.Where(x => QuestionKinds.Contains(x.Kind)).ToList();
        return new Count(questions.Count(x => x.Rejected && x.Retried == true), questions.Count);
    }

    /// <summary>Q-12: distinct verified quotes that carry a concrete cue, for one interview. Pooled, the rate is the yield per interview.</summary>
    public static Count YieldConcrete(IEnumerable<string> quotes) =>
        new(quotes.Distinct(StringComparer.Ordinal).Count(IndependentRules.HasConcreteDetail), 1);

    /// <summary>
    /// Q-13 (the redefined over-probe rate): an Example after a specific answer or a decline, over specific answers and declines. A
    /// ladder or a consequence after a specific answer is intended, so it is not over-probing and it is not counted here.
    /// </summary>
    public static Count OprMoves(IReadOnlyList<TurnFact> f, int substantiveWords)
    {
        var over = 0;
        var answers = 0;
        for (var i = 0; i < f.Count; i++)
        {
            var specific = !f[i].Vague && f[i].Words >= substantiveWords;
            if (!specific && !f[i].Terse) continue;
            answers++;
            if (i + 1 < f.Count && f[i + 1].Topic == f[i].Topic && f[i + 1].Move == MoveCodes.Example) over++;
        }
        return new Count(over, answers);
    }

    private static bool NoMoves(IReadOnlyList<TurnFact> f) => f.All(x => x.Move is null);

    /// <summary>True when a follow-up move on the same topic comes next, before the topic changes.</summary>
    private static bool FollowedOnSameTopic(IReadOnlyList<TurnFact> f, int i)
    {
        for (var j = i + 1; j < f.Count && f[j].Topic == f[i].Topic; j++)
            if (f[j].Move is { } m && MoveCodes.FollowUps.Contains(m)) return true;
        return false;
    }
}
