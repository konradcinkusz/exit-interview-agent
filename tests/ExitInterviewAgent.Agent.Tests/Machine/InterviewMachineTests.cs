using ExitInterviewAgent.Agent.Machine;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Tests.Support;
using ExitInterviewAgent.Records;
using static ExitInterviewAgent.Agent.Tests.Support.Helpers;

namespace ExitInterviewAgent.Agent.Tests.Machine;

/// <summary>
/// The interview machine under protocol 1.3 (spec §3.1–§3.5): the warm-up, the reason for leaving, the other topics, and the control and
/// safety branches. A plain reply is ten words with no cue, so it advances; <see cref="Vague"/> and <see cref="Specific"/> name the cue.
/// </summary>
public class InterviewMachineTests
{
    private static ReplySignals Plain(int words = 10, ConsentAnswer consent = ConsentAnswer.Yes) =>
        new(words, false, consent, false, false, false, false, false, false, 0);

    private static ReplySignals Vague() => Plain(6) with { Vague = true };

    private static ReplySignals Specific() => Plain(20) with { Concrete = true };

    /// <summary>The machine after consent and the warm-up answer: the reason-for-leaving question is out.</summary>
    private static InterviewMachine AtReason(InterviewProtocol? protocol = null)
    {
        var m = new InterviewMachine(protocol ?? Proto);
        m.Start();
        m.OnReply(Plain());
        m.OnReply(Plain());
        return m;
    }

    /// <summary>The machine with the first core topic after the reason for leaving (onboarding) asked.</summary>
    private static InterviewMachine AtOnboarding()
    {
        var m = AtReason();
        m.OnReply(Plain());
        return m;
    }

    /// <summary>Plays a whole interview from the opening, answering every step with <paramref name="reply"/>, and returns every step.</summary>
    private static List<Step> Whole(InterviewMachine m, Func<Step, ReplySignals> reply)
    {
        var steps = new List<Step>();
        var step = m.Start();
        while (true)
        {
            steps.Add(step);
            if (step.Kind is TurnKind.Close or TurnKind.Stop) return steps;
            step = m.OnReply(reply(step));
        }
    }

    [Fact]
    public void Start_issues_the_opening_turn_and_waits_for_consent()
    {
        var m = Started(out var first);

        Assert.Equal(new Step(TurnKind.Opening), first);
        Assert.Equal(Phase.AwaitingConsent, m.Phase);
        Assert.Equal(1, m.ConsentAsks);
    }

    [Fact]
    public void Start_twice_is_refused() => Assert.Throws<InvalidOperationException>(() => Started(out _).Start());

    [Fact]
    public void OnReply_before_start_is_refused() =>
        Assert.Throws<InvalidOperationException>(() => new InterviewMachine(Proto).OnReply(Signals()));

    [Fact]
    public void A_clear_yes_starts_with_the_warm_up()
    {
        var m = Started(out _);

        var step = m.OnReply(Signals(consent: ConsentAnswer.Yes));

        Assert.Equal(new Step(TurnKind.Topic, null, Move: Move.Warmup), step);
        Assert.Equal(Phase.AwaitingAnswer, m.Phase);
    }

    [Fact]
    public void A_no_at_the_consent_turn_stops_without_a_record()
    {
        var m = Started(out _);

        var step = m.OnReply(Signals(consent: ConsentAnswer.No));

        Assert.Equal(new Step(TurnKind.Stop, Stop: StopReason.ConsentDeclined), step);
        Assert.Equal(Phase.Stopped, m.Phase);
    }

    [Fact]
    public void A_withdrawal_phrase_at_the_consent_turn_is_a_withdrawal_even_if_the_rest_sounds_like_yes()
    {
        var m = Started(out _);

        var step = m.OnReply(Signals(withdrawal: true, consent: ConsentAnswer.Yes));

        Assert.Equal(StopReason.ConsentWithdrawn, step.Stop);
    }

    [Fact]
    public void An_unclear_answer_is_asked_once_more_and_a_second_unclear_answer_stops()
    {
        var m = Started(out _);

        var again = m.OnReply(Signals(consent: ConsentAnswer.Unclear));
        var stop = m.OnReply(Signals(consent: ConsentAnswer.Unclear));

        Assert.Equal(TurnKind.ConsentReask, again.Kind);
        Assert.Equal(Phase.Stopped, m.Phase);
        Assert.Equal(StopReason.ConsentUnclear, stop.Stop);
    }

    [Fact]
    public void An_unclear_answer_then_a_yes_continues_with_the_warm_up()
    {
        var m = Started(out _);
        m.OnReply(Signals(consent: ConsentAnswer.Unclear));

        var step = m.OnReply(Signals(consent: ConsentAnswer.Yes));

        Assert.Equal(new Step(TurnKind.Topic, null, Move: Move.Warmup), step);
    }

    [Fact]
    public void Topics_come_in_the_planned_order_and_the_interview_closes_after_the_last()
    {
        var m = new InterviewMachine(Proto);

        var steps = Whole(m, _ => Plain());

        var topics = steps.Where(s => s.Kind == TurnKind.Topic && s.Topic is not null).Select(s => s.Topic!.Value).Distinct().ToList();
        Assert.Equal(
            [Topic.ReasonForLeaving, Topic.Onboarding, Topic.Management, Topic.Growth, Topic.PayVsPromises, Topic.Culture],
            topics);
        Assert.Equal(new Step(TurnKind.Close, Close: CloseReason.AllTopicsCovered), steps[^1]);
        Assert.Equal(Phase.Closed, m.Phase);
        Assert.Equal(6, m.TopicsAsked);
    }

    [Fact]
    public void A_vague_answer_gets_one_example_probe_per_topic_and_then_the_topic_advances()
    {
        var m = AtOnboarding();

        var probe = m.OnReply(Vague());
        Assert.Equal(Phase.AwaitingProbeAnswer, m.Phase);
        var next = m.OnReply(Vague());

        Assert.Equal(new Step(TurnKind.Probe, Topic.Onboarding, Move: Move.Example), probe);
        Assert.Equal(new Step(TurnKind.Topic, Topic.Management, Move: Move.Open), next);
    }

    [Fact]
    public void The_probe_budget_resets_for_each_topic()
    {
        var m = AtOnboarding();
        m.OnReply(Vague());
        m.OnReply(Plain());

        var probe = m.OnReply(Vague());

        Assert.Equal(new Step(TurnKind.Probe, Topic.Management, Move: Move.Example), probe);
    }

    [Fact]
    public void A_plain_answer_is_not_probed() =>
        Assert.Equal(new Step(TurnKind.Topic, Topic.Onboarding, Move: Move.Open), AtReason().OnReply(Plain()));

    [Fact]
    public void The_follow_up_limit_comes_from_the_protocol()
    {
        var m = AtReason(Proto.WithLimits(Limits with { MaxFollowUpsReason = 1 }));

        var ladder = m.OnReply(Specific());
        var next = m.OnReply(Vague());

        Assert.Equal(new Step(TurnKind.Probe, Topic.ReasonForLeaving, Move: Move.Ladder), ladder);
        Assert.Equal(new Step(TurnKind.Topic, Topic.Onboarding, Move: Move.Open), next);
    }

    [Fact]
    public void A_named_person_is_redirected_once_per_topic_and_the_answer_then_counts()
    {
        var m = AtOnboarding();

        var redirect = m.OnReply(Plain() with { NamesPerson = true });
        var phaseAfterRedirect = m.Phase;
        var next = m.OnReply(Plain() with { NamesPerson = true });

        Assert.Equal(new Step(TurnKind.Redirect, Topic.Onboarding, Move: Move.Redirect), redirect);
        Assert.Equal(Phase.AwaitingRedirectAnswer, phaseAfterRedirect);
        Assert.Equal(new Step(TurnKind.Topic, Topic.Management, Move: Move.Open), next);
    }

    [Fact]
    public void A_vague_answer_after_a_redirect_can_still_be_probed()
    {
        var m = AtOnboarding();
        m.OnReply(Plain() with { NamesPerson = true });

        Assert.Equal(new Step(TurnKind.Probe, Topic.Onboarding, Move: Move.Example), m.OnReply(Vague()));
    }

    [Fact]
    public void A_contradiction_gets_one_clarification_and_no_more_probes_or_clarifications()
    {
        var m = AtOnboarding();
        m.OnReply(Vague());

        var clarify = m.OnReply(Plain() with { Contradiction = true });
        var next = m.OnReply(Plain() with { Contradiction = true, Vague = true });

        Assert.Equal(new Step(TurnKind.Clarification, Topic.Onboarding, Move: Move.Clarify), clarify);
        Assert.Equal(new Step(TurnKind.Topic, Topic.Management, Move: Move.Open), next);
    }

    [Fact]
    public void Consent_withdrawal_stops_at_once_in_every_dialogue_phase()
    {
        foreach (var setup in new Func<InterviewMachine>[]
                 {
                     () => { var m = Started(out _); m.OnReply(Plain()); return m; },
                     () => AtReason(),
                     () => AtOnboarding(),
                     () => { var m = AtOnboarding(); m.OnReply(Vague()); return m; },
                     () => { var m = AtOnboarding(); m.OnReply(Plain() with { NamesPerson = true }); return m; },
                     () => { var m = AtOnboarding(); m.OnReply(Vague()); m.OnReply(Plain() with { Contradiction = true }); return m; },
                 })
        {
            var m = setup();

            var step = m.OnReply(Plain() with { Withdrawal = true, Vague = true, NamesPerson = true, Hostile = true });

            Assert.Equal(new Step(TurnKind.Stop, Stop: StopReason.ConsentWithdrawn), step);
            Assert.Equal(Phase.Stopped, m.Phase);
            Assert.True(m.IsTerminal);
        }
    }

    [Fact]
    public void After_a_stop_no_further_reply_is_accepted()
    {
        var m = AtReason();
        m.OnReply(Plain() with { Withdrawal = true });

        Assert.Throws<InvalidOperationException>(() => m.OnReply(Plain()));
    }

    [Fact]
    public void After_a_close_no_further_reply_is_accepted()
    {
        var m = AtReason();
        for (var i = 0; i < 20 && !m.IsTerminal; i++) m.OnReply(Plain());
        Assert.Equal(Phase.Closed, m.Phase);

        Assert.Throws<InvalidOperationException>(() => m.OnReply(Plain()));
    }

    [Fact]
    public void Three_terse_answers_in_a_row_close_gracefully_and_a_substantive_one_resets_the_streak()
    {
        var m = AtReason();
        m.OnReply(Plain(words: 1) with { Terse = true });
        m.OnReply(Plain(words: 1) with { Terse = true });
        m.OnReply(Plain());
        m.OnReply(Plain(words: 1) with { Terse = true });
        m.OnReply(Plain(words: 1) with { Terse = true });

        var step = m.OnReply(Plain(words: 1) with { Terse = true });

        Assert.Equal(new Step(TurnKind.Close, Close: CloseReason.Unresponsive), step);
        Assert.Equal(Phase.Closed, m.Phase);
    }

    [Fact]
    public void The_first_hostile_reply_is_acknowledged_and_the_next_topic_follows_without_pressure()
    {
        var m = AtReason();

        var step = m.OnReply(Plain() with { Hostile = true, Vague = true });

        Assert.Equal(new Step(TurnKind.Topic, Topic.Onboarding, Preface.AcknowledgeFrustration, Move: Move.Open), step);
        Assert.Equal(0, m.ProbesUsed);
    }

    [Fact]
    public void The_second_hostile_reply_closes_gracefully()
    {
        var m = AtReason();
        m.OnReply(Plain() with { Hostile = true });

        var step = m.OnReply(Plain() with { Hostile = true });

        Assert.Equal(new Step(TurnKind.Close, Close: CloseReason.Hostile), step);
    }

    [Fact]
    public void An_exhausted_model_budget_closes_gracefully()
    {
        var step = AtReason().OnReply(Plain(), budgetExhausted: true);

        Assert.Equal(new Step(TurnKind.Close, Close: CloseReason.BudgetExhausted), step);
    }

    [Fact]
    public void The_interviewer_turn_budget_closes_gracefully()
    {
        var m = new InterviewMachine(Proto.WithLimits(Limits with { MaxInterviewerTurns = 3 }));
        m.Start();
        m.OnReply(Signals());
        m.OnReply(Signals());

        var step = m.OnReply(Signals());

        Assert.Equal(new Step(TurnKind.Close, Close: CloseReason.BudgetExhausted), step);
    }

    [Fact]
    public void A_disconnect_stops_without_a_record_and_works_in_any_phase()
    {
        var m = AtReason();

        var step = m.OnDisconnect();

        Assert.Equal(new Step(TurnKind.Stop, Stop: StopReason.Disconnected), step);
        Assert.Equal(Phase.Stopped, m.Phase);
    }

    [Fact]
    public void Withdrawal_wins_over_every_other_signal_in_the_same_reply()
    {
        var m = AtReason();

        var step = m.OnReply(Signals(withdrawal: true, hostile: true, names: true, vague: true, contradiction: true, terse: true));

        Assert.Equal(StopReason.ConsentWithdrawn, step.Stop);
    }

    [Fact]
    public void Interviewer_turns_are_counted_for_every_issued_step()
    {
        var m = Started(out _);
        m.OnReply(Signals());
        m.OnReply(Signals(vague: true));

        Assert.Equal(3, m.InterviewerTurns);
    }
}
