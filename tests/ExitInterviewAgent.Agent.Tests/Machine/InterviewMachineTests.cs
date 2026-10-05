using ExitInterviewAgent.Agent.Machine;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Tests.Support;
using ExitInterviewAgent.Records;
using static ExitInterviewAgent.Agent.Tests.Support.Helpers;

namespace ExitInterviewAgent.Agent.Tests.Machine;

public class InterviewMachineTests
{
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
    public void A_clear_yes_starts_the_first_topic()
    {
        var m = Started(out _);

        var step = m.OnReply(Signals(consent: ConsentAnswer.Yes));

        Assert.Equal(new Step(TurnKind.Topic, Topic.Onboarding), step);
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
    public void An_unclear_answer_then_a_yes_continues()
    {
        var m = Started(out _);
        m.OnReply(Signals(consent: ConsentAnswer.Unclear));

        var step = m.OnReply(Signals(consent: ConsentAnswer.Yes));

        Assert.Equal(new Step(TurnKind.Topic, Topic.Onboarding), step);
    }

    [Fact]
    public void Topics_come_in_the_fixed_order_and_the_interview_closes_after_the_sixth()
    {
        var m = AtFirstTopic();
        var seen = new List<Topic> { Topic.Onboarding };

        Step step;
        do
        {
            step = m.OnReply(Signals());
            if (step.Kind == TurnKind.Topic) seen.Add(step.Topic!.Value);
        }
        while (step.Kind == TurnKind.Topic);

        Assert.Equal([Topic.Onboarding, Topic.Management, Topic.Growth, Topic.PayVsPromises, Topic.Culture, Topic.ReasonForLeaving], seen);
        Assert.Equal(new Step(TurnKind.Close, Close: CloseReason.AllTopicsCovered), step);
        Assert.Equal(Phase.Closed, m.Phase);
        Assert.Equal(6, m.TopicsAsked);
    }

    [Fact]
    public void A_vague_answer_gets_exactly_one_probe_per_topic()
    {
        var m = AtFirstTopic();

        var probe = m.OnReply(Signals(vague: true));
        var next = m.OnReply(Signals(vague: true));

        Assert.Equal(new Step(TurnKind.Probe, Topic.Onboarding), probe);
        Assert.Equal(Phase.AwaitingAnswer, m.Phase);
        Assert.Equal(new Step(TurnKind.Topic, Topic.Management), next);
    }

    [Fact]
    public void The_probe_budget_resets_for_each_topic()
    {
        var m = AtFirstTopic();
        m.OnReply(Signals(vague: true));
        m.OnReply(Signals());

        var probe = m.OnReply(Signals(vague: true));

        Assert.Equal(new Step(TurnKind.Probe, Topic.Management), probe);
    }

    [Fact]
    public void A_non_vague_answer_is_not_probed() =>
        Assert.Equal(TurnKind.Topic, AtFirstTopic().OnReply(Signals(vague: false)).Kind);

    [Fact]
    public void The_probe_limit_comes_from_the_protocol()
    {
        var two = new InterviewMachine(Proto.WithLimits(Limits with { MaxProbesPerTopic = 2 }));
        two.Start();
        two.OnReply(Signals());

        Assert.Equal(TurnKind.Probe, two.OnReply(Signals(vague: true)).Kind);
        Assert.Equal(TurnKind.Probe, two.OnReply(Signals(vague: true)).Kind);
        Assert.Equal(TurnKind.Topic, two.OnReply(Signals(vague: true)).Kind);
    }

    [Fact]
    public void A_named_person_is_redirected_once_per_topic_and_the_answer_then_counts()
    {
        var m = AtFirstTopic();

        var redirect = m.OnReply(Signals(names: true));
        var phaseAfterRedirect = m.Phase;
        var next = m.OnReply(Signals(names: true));

        Assert.Equal(new Step(TurnKind.Redirect, Topic.Onboarding), redirect);
        Assert.Equal(Phase.AwaitingRedirectAnswer, phaseAfterRedirect);
        Assert.Equal(new Step(TurnKind.Topic, Topic.Management), next);
    }

    [Fact]
    public void A_vague_answer_after_a_redirect_can_still_be_probed()
    {
        var m = AtFirstTopic();
        m.OnReply(Signals(names: true));

        Assert.Equal(TurnKind.Probe, m.OnReply(Signals(vague: true)).Kind);
    }

    [Fact]
    public void A_contradiction_gets_one_clarification_and_no_more_probes_or_clarifications()
    {
        var m = AtFirstTopic();
        m.OnReply(Signals(vague: true));

        var clarify = m.OnReply(Signals(contradiction: true));
        var next = m.OnReply(Signals(contradiction: true, vague: true));

        Assert.Equal(new Step(TurnKind.Clarification, Topic.Onboarding), clarify);
        Assert.Equal(new Step(TurnKind.Topic, Topic.Management), next);
    }

    [Fact]
    public void Consent_withdrawal_stops_at_once_in_every_dialogue_phase()
    {
        foreach (var setup in new Func<InterviewMachine>[]
                 {
                     AtFirstTopic,
                     () => { var m = AtFirstTopic(); m.OnReply(Signals(vague: true)); return m; },
                     () => { var m = AtFirstTopic(); m.OnReply(Signals(names: true)); return m; },
                     () => { var m = AtFirstTopic(); m.OnReply(Signals(vague: true)); m.OnReply(Signals(contradiction: true)); return m; },
                 })
        {
            var m = setup();

            var step = m.OnReply(Signals(withdrawal: true, vague: true, names: true, hostile: true));

            Assert.Equal(new Step(TurnKind.Stop, Stop: StopReason.ConsentWithdrawn), step);
            Assert.Equal(Phase.Stopped, m.Phase);
            Assert.True(m.IsTerminal);
        }
    }

    [Fact]
    public void After_a_stop_no_further_reply_is_accepted()
    {
        var m = AtFirstTopic();
        m.OnReply(Signals(withdrawal: true));

        Assert.Throws<InvalidOperationException>(() => m.OnReply(Signals()));
    }

    [Fact]
    public void After_a_close_no_further_reply_is_accepted()
    {
        var m = AtFirstTopic();
        for (var i = 0; i < 6; i++) m.OnReply(Signals());

        Assert.Throws<InvalidOperationException>(() => m.OnReply(Signals()));
    }

    [Fact]
    public void Three_terse_answers_in_a_row_close_gracefully_and_a_substantive_one_resets_the_streak()
    {
        var m = AtFirstTopic();
        m.OnReply(Signals(words: 1, terse: true));
        m.OnReply(Signals(words: 1, terse: true));
        m.OnReply(Signals());
        m.OnReply(Signals(words: 1, terse: true));
        m.OnReply(Signals(words: 1, terse: true));

        var step = m.OnReply(Signals(words: 1, terse: true));

        Assert.Equal(new Step(TurnKind.Close, Close: CloseReason.Unresponsive), step);
        Assert.Equal(Phase.Closed, m.Phase);
    }

    [Fact]
    public void The_first_hostile_reply_is_acknowledged_and_the_next_topic_follows_without_pressure()
    {
        var m = AtFirstTopic();

        var step = m.OnReply(Signals(hostile: true, vague: true));

        Assert.Equal(new Step(TurnKind.Topic, Topic.Management, Preface.AcknowledgeFrustration), step);
        Assert.Equal(0, m.ProbesUsed);
    }

    [Fact]
    public void The_second_hostile_reply_closes_gracefully()
    {
        var m = AtFirstTopic();
        m.OnReply(Signals(hostile: true));

        var step = m.OnReply(Signals(hostile: true));

        Assert.Equal(new Step(TurnKind.Close, Close: CloseReason.Hostile), step);
    }

    [Fact]
    public void An_exhausted_model_budget_closes_gracefully()
    {
        var step = AtFirstTopic().OnReply(Signals(), budgetExhausted: true);

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
        var m = AtFirstTopic();

        var step = m.OnDisconnect();

        Assert.Equal(new Step(TurnKind.Stop, Stop: StopReason.Disconnected), step);
        Assert.Equal(Phase.Stopped, m.Phase);
    }

    [Fact]
    public void Withdrawal_wins_over_every_other_signal_in_the_same_reply()
    {
        var m = AtFirstTopic();

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
