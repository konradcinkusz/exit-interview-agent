using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Runner;
using ExitInterviewAgent.Agent.Tracing;
using ExitInterviewAgent.Eval.Execution;
using ExitInterviewAgent.Eval.Layer1;
using ExitInterviewAgent.Eval.Stats;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Eval.Tests;

/// <summary>
/// The WP6 turn metrics (spec §6.1 Q-01..Q-04, Q-09, Q-12, Q-13) on SYNTHETIC traces that carry <c>interview.turn.move</c> and
/// <c>interview.turn.preface</c>. Every metric has a crafted bad trace that must produce the bad count; a metric that cannot fail is not a metric.
/// </summary>
public class TurnMetricsTests
{
    private const string MoveKey = "interview.turn.move";
    private const int Sw = 12;

    /// <summary>One interviewer turn and the reply to it, as the agent's trace states it (§5.2).</summary>
    private static CapturedSpan T(int id, string kind, string? topic, string? move, int words, bool vague = false, bool terse = false, string? preface = null, bool rejected = false, bool? retried = null)
    {
        var tags = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [InterviewTelemetry.Attr.TurnKind] = kind,
            [InterviewTelemetry.Attr.ReplyWords] = words,
            [InterviewTelemetry.Attr.SignalVague] = vague,
            [InterviewTelemetry.Attr.SignalTerse] = terse,
        };
        if (topic is not null) tags[InterviewTelemetry.Attr.Topic] = topic;
        if (move is not null) tags[MoveKey] = move;
        if (preface is not null) tags["interview.turn.preface"] = preface;
        if (retried is { } r) tags["interview.question.retried"] = r;
        IReadOnlyList<CapturedEvent> events = rejected
            ? [new CapturedEvent(InterviewTelemetry.Events.QuestionRejected, 0, new Dictionary<string, object?>(StringComparer.Ordinal) { ["reason"] = "double_barrelled" })]
            : [];
        return new CapturedSpan(id, null, InterviewTelemetry.Spans.Turn, id * 1000L, 1000, "Unset", null, tags, events);
    }

    private static IReadOnlyList<TurnFact> Facts(params CapturedSpan[] turns) => TurnFacts.FromTrace(new CapturedTrace(turns));

    /// <summary>The rating digit is not in the trace; the grader reads it from the raw reply and sets it on the facts.</summary>
    private static IReadOnlyList<TurnFact> Rated(IReadOnlyList<TurnFact> f, params int?[] ratings) => f.Select((x, i) => x with { Scale = ratings[i] }).ToList();

    // ---- the move codes are the agent's enum, spelled as the trace spells them ------------------------------------------------

    [Fact]
    public void The_move_codes_are_the_snake_case_of_the_agents_move_enum()
    {
        var expected = Enum.GetValues<Move>().Select(m => SpanTags.Snake(m.ToString())).Order(StringComparer.Ordinal);

        Assert.Equal(expected, MoveCodes.All.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_move_code_outside_the_enum_is_rejected_so_a_typo_cannot_hide_a_move()
    {
        Assert.Null(MoveCodes.Parse("scale_followup"));
        Assert.Equal(MoveCodes.ScaleFollowUp, MoveCodes.Parse("scale_follow_up"));
    }

    // ---- Q-01 order_follows_salience -------------------------------------------------------------------------------------------

    [Fact]
    public void Q01_the_first_core_topic_after_the_reason_is_the_salient_one()
    {
        var good = Facts(
            T(1, "topic", null, "warmup", 20),
            T(2, "topic", "reason_for_leaving", "open", 30),
            T(3, "topic", "pay_vs_promises", "open", 40),
            T(4, "topic", "onboarding", "open", 40));

        Assert.Equal(new Count(1, 1), TurnMetrics.OrderFollowsSalience(good, "pay_vs_promises"));
    }

    [Fact]
    public void Q01_fails_on_a_trace_that_opens_on_another_topic_before_the_salient_one()
    {
        var bad = Facts(
            T(1, "topic", "reason_for_leaving", "open", 30),
            T(2, "topic", "onboarding", "open", 40),
            T(3, "topic", "pay_vs_promises", "open", 40));

        Assert.Equal(new Count(0, 1), TurnMetrics.OrderFollowsSalience(bad, "pay_vs_promises"));
    }

    [Fact]
    public void Q01_measures_nothing_for_a_persona_without_a_declared_salient_topic()
    {
        var f = Facts(T(1, "topic", "reason_for_leaving", "open", 30), T(2, "topic", "culture", "open", 30));

        Assert.Equal(Count.Zero, TurnMetrics.OrderFollowsSalience(f, null));
    }

    // ---- Q-02 probe_on_core ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Q02_a_substantive_core_topic_with_an_example_follow_up_counts_as_probed()
    {
        var good = Facts(
            T(1, "topic", "onboarding", "open", 20),
            T(2, "probe", "onboarding", "example", 25));

        Assert.Equal(new Count(1, 1), TurnMetrics.ProbeOnCore(good, Sw));
    }

    [Fact]
    public void Q02_fails_on_a_substantive_core_topic_that_is_left_without_a_follow_up()
    {
        var bad = Facts(
            T(1, "topic", "onboarding", "open", 20),
            T(2, "topic", "management", "open", 20));

        Assert.Equal(new Count(0, 2), TurnMetrics.ProbeOnCore(bad, Sw));
    }

    [Fact]
    public void Q02_a_short_reply_is_not_in_the_denominator()
    {
        var f = Facts(T(1, "topic", "onboarding", "open", 4));

        Assert.Equal(Count.Zero, TurnMetrics.ProbeOnCore(f, Sw));
    }

    // ---- Q-03 no_probe_on_light_mid --------------------------------------------------------------------------------------------

    [Fact]
    public void Q03_a_light_topic_rated_three_with_no_follow_up_is_released()
    {
        var good = Rated(Facts(T(1, "topic", "growth", "scale", 3), T(2, "topic", "culture", "scale", 3)), 3, 4);

        Assert.Equal(new Count(2, 2), TurnMetrics.NoProbeOnLightMid(good));
    }

    [Fact]
    public void Q03_fails_when_a_light_topic_rated_four_gets_a_scale_follow_up()
    {
        var bad = Rated(Facts(T(1, "topic", "growth", "scale", 3), T(2, "probe", "growth", "scale_follow_up", 20)), 4, null);

        Assert.Equal(new Count(0, 1), TurnMetrics.NoProbeOnLightMid(bad));
    }

    [Fact]
    public void Q03_ignores_extreme_ratings_which_are_the_scale_follow_up_case()
    {
        var f = Rated(Facts(T(1, "topic", "growth", "scale", 3)), 1);

        Assert.Equal(Count.Zero, TurnMetrics.NoProbeOnLightMid(f));
    }

    // ---- Q-04 questions_within_cap ---------------------------------------------------------------------------------------------

    [Fact]
    public void Q04_a_talkative_interview_within_eighteen_questions_passes()
    {
        var f = Facts(Enumerable.Range(1, 16).Select(i => T(i, "topic", "culture", "open", 30)).ToArray());

        Assert.Equal(new Count(1, 1), TurnMetrics.QuestionsWithinCap(f, 18));
    }

    [Fact]
    public void Q04_fails_when_a_talkative_interview_runs_to_nineteen_questions()
    {
        var f = Facts(Enumerable.Range(1, 19).Select(i => T(i, "topic", "culture", "open", 30)).ToArray());

        Assert.Equal(new Count(0, 1), TurnMetrics.QuestionsWithinCap(f, 18));
    }

    [Fact]
    public void Q04_does_not_count_the_opening_or_the_close()
    {
        var f = Facts(T(1, "opening", null, null, 3), T(2, "topic", "culture", "open", 30), T(3, "close", null, null, 0));

        Assert.Equal(new Count(1, 1), TurnMetrics.QuestionsWithinCap(f, 1));
    }

    [Fact]
    public void Q04_measures_nothing_without_a_cap()
    {
        Assert.Equal(Count.Zero, TurnMetrics.QuestionsWithinCap(Facts(T(1, "topic", "culture", "open", 30)), null));
    }

    // ---- Q-09 replaced_rate ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Q09_a_question_rejected_again_after_the_retry_is_replaced()
    {
        var f = Facts(
            T(1, "topic", "culture", "open", 30),
            T(2, "probe", "culture", "example", 20, rejected: true, retried: true));

        Assert.Equal(new Count(1, 2), TurnMetrics.ReplacedRate(f));
    }

    [Fact]
    public void Q09_a_rejection_that_was_not_retried_is_not_counted_as_replaced()
    {
        var f = Facts(T(1, "topic", "culture", "open", 30, rejected: true, retried: false));

        Assert.Equal(new Count(0, 1), TurnMetrics.ReplacedRate(f));
    }

    [Fact]
    public void Q09_counts_every_replaced_question_when_all_of_them_were_replaced()
    {
        var bad = Facts(T(1, "topic", "culture", "open", 30, rejected: true, retried: true), T(2, "topic", "growth", "open", 30, rejected: true, retried: true));

        Assert.Equal(new Count(2, 2), TurnMetrics.ReplacedRate(bad));
    }

    // ---- Q-12 yield_concrete ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Q12_counts_distinct_quotes_with_a_concrete_cue_and_ignores_duplicates_and_vague_quotes()
    {
        var quotes = new[] { "Laptop przyszedł w 2024 roku, po pięciu dniach", "Laptop przyszedł w 2024 roku, po pięciu dniach", "Było w porządku" };

        Assert.Equal(new Count(1, 1), TurnMetrics.YieldConcrete(quotes));
    }

    [Fact]
    public void Q12_a_record_with_only_vague_quotes_yields_nothing()
    {
        Assert.Equal(new Count(0, 1), TurnMetrics.YieldConcrete(["Było w porządku", "Różnie bywało"]));
    }

    // ---- Q-13 opr_moves --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Q13_an_example_after_a_specific_answer_is_an_over_probe()
    {
        var f = Facts(T(1, "topic", "culture", "open", 30), T(2, "probe", "culture", "example", 20));

        Assert.Equal(new Count(1, 1), TurnMetrics.OprMoves(f, Sw));
    }

    [Fact]
    public void Q13_an_example_after_a_vague_answer_is_intended_and_not_in_the_denominator()
    {
        var f = Facts(T(1, "topic", "culture", "open", 30, vague: true), T(2, "probe", "culture", "example", 20));

        Assert.Equal(Count.Zero, TurnMetrics.OprMoves(f, Sw));
    }

    [Fact]
    public void Q13_a_ladder_or_consequence_after_a_specific_answer_is_intended_and_does_not_count_as_over_probing()
    {
        var f = Facts(T(1, "topic", "culture", "open", 30), T(2, "probe", "culture", "ladder", 20));

        Assert.Equal(new Count(0, 1), TurnMetrics.OprMoves(f, Sw));
    }

    [Fact]
    public void Q13_a_decline_followed_by_an_example_is_an_over_probe()
    {
        var f = Facts(T(1, "topic", "culture", "open", 1, terse: true), T(2, "probe", "culture", "example", 20));

        Assert.Equal(new Count(1, 1), TurnMetrics.OprMoves(f, Sw));
    }

    // ---- the rating digit comes from the reply to the question, and only when the turns line up ----------------------------

    [Fact]
    public void The_rating_digit_is_read_from_the_reply_to_each_scale_question()
    {
        var tr = new Transcript();
        tr.Add(Speaker.Interviewer, TurnKind.Topic, null, "Na początek coś prostego.");
        tr.Add(Speaker.Interviewee, TurnKind.Topic, null, "Dobrze.");
        tr.Add(Speaker.Interviewer, TurnKind.Topic, Topic.Growth, "W skali od 1 do 5 jak wypada rozwój?");
        tr.Add(Speaker.Interviewee, TurnKind.Topic, Topic.Growth, "4");
        var facts = Facts(T(1, "topic", null, "warmup", 2), T(2, "topic", "growth", "scale", 1));

        var rated = TurnFacts.WithRatings(facts, tr);

        Assert.Null(rated[0].Scale);
        Assert.Equal(4, rated[1].Scale);
    }

    [Fact]
    public void When_the_trace_and_the_transcript_do_not_line_up_no_rating_is_set_rather_than_a_wrong_one()
    {
        var tr = new Transcript();
        tr.Add(Speaker.Interviewer, TurnKind.Topic, Topic.Growth, "W skali od 1 do 5?");
        tr.Add(Speaker.Interviewee, TurnKind.Topic, Topic.Growth, "2");
        tr.Add(Speaker.Interviewer, TurnKind.Topic, Topic.Culture, "Druga pytanie?");
        var facts = Facts(T(1, "topic", "growth", "scale", 1));

        Assert.Null(TurnFacts.WithRatings(facts, tr)[0].Scale);
        Assert.Null(TurnFacts.WithRatings(facts, null)[0].Scale);
    }

    // ---- facts from the trace ---------------------------------------------------------------------------------------------------

    [Fact]
    public void A_turn_without_move_or_preface_attributes_reads_as_absent_so_today_s_traces_measure_nothing_new()
    {
        var f = Facts(T(1, "topic", "culture", null, 30));

        Assert.Null(f[0].Move);
        Assert.Null(f[0].Preface);
        Assert.Equal(Count.Zero, TurnMetrics.OrderFollowsSalience(f, "culture"));
    }

    [Fact]
    public void A_preface_is_read_from_its_own_attribute()
    {
        var f = Facts(T(1, "probe", "culture", "example", 20, preface: "alreadySaid"));

        Assert.Equal("alreadySaid", f[0].Preface);
    }
}
