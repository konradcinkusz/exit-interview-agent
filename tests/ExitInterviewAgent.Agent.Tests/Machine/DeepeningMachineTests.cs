using ExitInterviewAgent.Agent.Machine;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Tests.Support;
using ExitInterviewAgent.Records;
using static ExitInterviewAgent.Agent.Tests.Support.Helpers;

namespace ExitInterviewAgent.Agent.Tests.Machine;

/// <summary>The deepening phase (Y2): a serious reply opens up to <c>maxDeepProbesPerTopic</c> neutral follow-ups, one per menu element.</summary>
public class DeepeningMachineTests
{
    private static int Bit(DeepFocus f) => 1 << (int)f;

    [Fact]
    public void A_serious_reply_opens_deepening_on_the_first_menu_element_with_the_one_time_reminder()
    {
        var m = AtFirstTopic();

        var step = m.OnReply(Signals(serious: true));

        Assert.Equal(TurnKind.DeepProbe, step.Kind);
        Assert.Equal(Topic.Onboarding, step.Topic);
        Assert.Equal(DeepFocus.WhatHappened, step.Focus);
        Assert.Equal(Preface.DeepeningReminder, step.Preface);
        Assert.Equal(Phase.AwaitingDeepAnswer, m.Phase);
        Assert.Equal(0, m.ProbesUsed);
    }

    [Fact]
    public void Each_later_deep_step_asks_the_next_element_and_the_reminder_is_said_only_once()
    {
        var m = AtFirstTopic();
        m.OnReply(Signals(serious: true));

        var second = m.OnReply(Signals());
        var third = m.OnReply(Signals());

        Assert.Equal(DeepFocus.WhenHowOften, second.Focus);
        Assert.Equal(Preface.None, second.Preface);
        Assert.Equal(DeepFocus.WhoByRole, third.Focus);
        Assert.Equal(Preface.None, third.Preface);
    }

    [Fact]
    public void Deepening_on_a_topic_stops_at_the_configured_limit_and_the_topic_then_advances()
    {
        var m = AtFirstTopic();
        var steps = new List<Step> { m.OnReply(Signals(serious: true)) };
        for (var i = 1; i < Proto.Limits.MaxDeepProbesPerTopic; i++) steps.Add(m.OnReply(Signals()));

        var after = m.OnReply(Signals());

        Assert.Equal(4, Proto.Limits.MaxDeepProbesPerTopic);
        Assert.Equal(Proto.Limits.MaxDeepProbesPerTopic, steps.Count);
        Assert.All(steps, s => Assert.Equal(TurnKind.DeepProbe, s.Kind));
        Assert.Equal(new Step(TurnKind.Topic, Topic.Management), after);
    }

    [Fact]
    public void An_element_the_reply_already_described_is_not_asked_again()
    {
        var m = AtFirstTopic();

        var step = m.OnReply(Signals(serious: true, covered: Bit(DeepFocus.WhatHappened)));

        Assert.Equal(DeepFocus.WhenHowOften, step.Focus);
    }

    [Fact]
    public void An_element_asked_earlier_is_not_asked_again_even_when_the_next_reply_is_silent()
    {
        var m = AtFirstTopic();
        m.OnReply(Signals(serious: true));

        var step = m.OnReply(Signals(covered: Bit(DeepFocus.WhenHowOften)));

        Assert.Equal(DeepFocus.WhoByRole, step.Focus);
    }

    [Fact]
    public void A_deep_step_does_not_spend_the_vague_probe_budget()
    {
        var m = AtFirstTopic();
        m.OnReply(Signals(serious: true));
        m.OnReply(Signals());

        Assert.Equal(0, m.ProbesUsed);
    }

    [Fact]
    public void A_non_serious_reply_on_a_topic_still_advances_as_before()
    {
        var m = AtFirstTopic();

        var step = m.OnReply(Signals());

        Assert.Equal(new Step(TurnKind.Topic, Topic.Management), step);
        Assert.False(m.Deepening);
    }

    [Fact]
    public void A_vague_reply_still_gets_its_one_probe_and_is_not_deepened()
    {
        var m = AtFirstTopic();

        var step = m.OnReply(Signals(vague: true));

        Assert.Equal(TurnKind.Probe, step.Kind);
    }

    [Fact]
    public void Withdrawal_during_deepening_stops_the_interview_at_once()
    {
        var m = AtFirstTopic();
        m.OnReply(Signals(serious: true));

        var step = m.OnReply(Signals(withdrawal: true));

        Assert.Equal(new Step(TurnKind.Stop, Stop: StopReason.ConsentWithdrawn), step);
        Assert.Equal(Phase.Stopped, m.Phase);
    }

    [Fact]
    public void Hostile_reply_during_deepening_releases_the_interviewee_and_ends_the_deepening()
    {
        var m = AtFirstTopic();
        m.OnReply(Signals(serious: true));

        var step = m.OnReply(Signals(hostile: true));

        Assert.Equal(new Step(TurnKind.Topic, Topic.Management, Preface.AcknowledgeFrustration), step);
        Assert.False(m.Deepening);
    }

    [Fact]
    public void A_name_during_deepening_is_redirected_and_deepening_resumes_afterwards()
    {
        var m = AtFirstTopic();
        m.OnReply(Signals(serious: true));

        var redirect = m.OnReply(Signals(names: true));
        var resumed = m.OnReply(Signals());

        Assert.Equal(new Step(TurnKind.Redirect, Topic.Onboarding), redirect);
        Assert.Equal(TurnKind.DeepProbe, resumed.Kind);
        Assert.Equal(DeepFocus.WhenHowOften, resumed.Focus);
    }

    [Fact]
    public void Deepening_never_goes_past_the_interviewer_turn_budget()
    {
        var protocol = Proto.WithLimits(Limits with { MaxInterviewerTurns = 3 });
        var m = new InterviewMachine(protocol);
        m.Start();
        m.OnReply(Signals(consent: ConsentAnswer.Yes));
        m.OnReply(Signals(serious: true));

        var step = m.OnReply(Signals());

        Assert.Equal(TurnKind.Close, step.Kind);
        Assert.Equal(CloseReason.BudgetExhausted, step.Close);
    }

    [Fact]
    public void Deepening_starts_again_on_the_next_topic_but_the_reminder_is_not_repeated()
    {
        var m = AtFirstTopic();
        m.OnReply(Signals(serious: true));
        for (var i = 0; i < Proto.Limits.MaxDeepProbesPerTopic - 1; i++) m.OnReply(Signals());
        m.OnReply(Signals());

        var nextTopicSerious = m.OnReply(Signals(serious: true));

        Assert.Equal(TurnKind.DeepProbe, nextTopicSerious.Kind);
        Assert.Equal(Topic.Management, nextTopicSerious.Topic);
        Assert.Equal(DeepFocus.WhatHappened, nextTopicSerious.Focus);
        Assert.Equal(Preface.None, nextTopicSerious.Preface);
    }

    [Fact]
    public void Without_a_serious_signal_the_machine_sequence_is_the_protocol_one()
    {
        var m = AtFirstTopic();

        var kinds = new List<TurnKind> { m.OnReply(Signals(vague: true)).Kind, m.OnReply(Signals()).Kind, m.OnReply(Signals()).Kind };

        Assert.Equal([TurnKind.Probe, TurnKind.Topic, TurnKind.Topic], kinds);
    }

    [Fact]
    public void The_menu_order_is_the_protocol_order_and_no_element_repeats()
    {
        Assert.Equal(
            [DeepFocus.WhatHappened, DeepFocus.WhenHowOften, DeepFocus.WhoByRole, DeepFocus.WhatTheyDidAndResponse, DeepFocus.HowItEndedAndMeaning],
            Enum.GetValues<DeepFocus>());
        Assert.Equal(5, Proto.DeepeningSeeds.Count);
    }
}
