using ExitInterviewAgent.Agent.Machine;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Tests.Support;
using ExitInterviewAgent.Records;
using static ExitInterviewAgent.Agent.Tests.Support.Helpers;

namespace ExitInterviewAgent.Agent.Tests.Machine;

/// <summary>
/// The deepening phase under protocol 1.3 (spec §3.7): a serious account on a topic opens up to <c>MaxDeepProbesPerTopic</c> neutral
/// follow-ups, one per menu element. The warm-up never deepens; the first deepening question is on the first topic after it.
/// </summary>
public class DeepeningMachineTests
{
    private static int Bit(DeepFocus f) => 1 << (int)f;

    private static ReplySignals Plain(int words = 10) => new(words, false, ConsentAnswer.Yes, false, false, false, false, false, false, 0);

    private static ReplySignals Vague() => Plain(6) with { Vague = true };

    /// <summary>The machine after consent and the warm-up answer: the reason-for-leaving question is out.</summary>
    private static InterviewMachine AtReason(InterviewProtocol? protocol = null)
    {
        var m = new InterviewMachine(protocol ?? Proto);
        m.Start();
        m.OnReply(Plain());
        m.OnReply(Plain());
        return m;
    }

    /// <summary>The machine with onboarding asked: the first core topic after the reason for leaving.</summary>
    private static InterviewMachine AtOnboarding()
    {
        var m = AtReason();
        m.OnReply(Plain());
        return m;
    }

    [Fact]
    public void A_serious_reply_opens_deepening_on_the_first_menu_element_with_the_one_time_reminder()
    {
        var m = AtOnboarding();

        var step = m.OnReply(Plain() with { Serious = true });

        Assert.Equal(TurnKind.DeepProbe, step.Kind);
        Assert.Equal(Topic.Onboarding, step.Topic);
        Assert.Equal(DeepFocus.WhatHappened, step.Focus);
        Assert.Equal(Preface.DeepeningReminder, step.Preface);
        Assert.Equal(Move.Deep, step.Move);
        Assert.Equal(Phase.AwaitingDeepAnswer, m.Phase);
        Assert.Equal(0, m.ProbesUsed);
    }

    [Fact]
    public void Each_later_deep_step_asks_the_next_element_and_the_reminder_is_said_only_once()
    {
        var m = AtOnboarding();
        m.OnReply(Plain() with { Serious = true });

        var second = m.OnReply(Plain());
        var third = m.OnReply(Plain());

        Assert.Equal(DeepFocus.WhenHowOften, second.Focus);
        Assert.Equal(Preface.None, second.Preface);
        Assert.Equal(DeepFocus.WhoByRole, third.Focus);
        Assert.Equal(Preface.None, third.Preface);
    }

    [Fact]
    public void Deepening_on_a_topic_stops_at_the_configured_limit_and_the_topic_then_advances()
    {
        var m = AtOnboarding();
        var steps = new List<Step> { m.OnReply(Plain() with { Serious = true }) };
        for (var i = 1; i < Proto.Limits.MaxDeepProbesPerTopic; i++) steps.Add(m.OnReply(Plain()));

        var after = m.OnReply(Plain());

        Assert.Equal(3, Proto.Limits.MaxDeepProbesPerTopic);
        Assert.Equal(Proto.Limits.MaxDeepProbesPerTopic, steps.Count);
        Assert.All(steps, s => Assert.Equal(TurnKind.DeepProbe, s.Kind));
        Assert.Equal(new Step(TurnKind.Topic, Topic.Management, Move: Move.Open), after);
    }

    [Fact]
    public void An_element_the_reply_already_described_is_not_asked_again()
    {
        var m = AtOnboarding();

        var step = m.OnReply(Plain() with { Serious = true, DeepCovered = Bit(DeepFocus.WhatHappened) });

        Assert.Equal(DeepFocus.WhenHowOften, step.Focus);
    }

    [Fact]
    public void An_element_asked_earlier_is_not_asked_again_even_when_the_next_reply_is_silent()
    {
        var m = AtOnboarding();
        m.OnReply(Plain() with { Serious = true });

        var step = m.OnReply(Plain() with { DeepCovered = Bit(DeepFocus.WhenHowOften) });

        Assert.Equal(DeepFocus.WhoByRole, step.Focus);
    }

    [Fact]
    public void A_deep_step_does_not_spend_the_vague_probe_budget()
    {
        var m = AtOnboarding();
        m.OnReply(Plain() with { Serious = true });
        m.OnReply(Plain());

        Assert.Equal(0, m.ProbesUsed);
    }

    [Fact]
    public void A_non_serious_reply_on_a_topic_still_advances_as_before()
    {
        var m = AtOnboarding();

        var step = m.OnReply(Plain());

        Assert.Equal(new Step(TurnKind.Topic, Topic.Management, Move: Move.Open), step);
        Assert.False(m.Deepening);
    }

    [Fact]
    public void A_vague_reply_still_gets_its_one_probe_and_is_not_deepened()
    {
        var m = AtOnboarding();

        var step = m.OnReply(Vague());

        Assert.Equal(new Step(TurnKind.Probe, Topic.Onboarding, Move: Move.Example), step);
        Assert.False(m.Deepening);
    }

    [Fact]
    public void Withdrawal_during_deepening_stops_the_interview_at_once()
    {
        var m = AtOnboarding();
        m.OnReply(Plain() with { Serious = true });

        var step = m.OnReply(Plain() with { Withdrawal = true });

        Assert.Equal(new Step(TurnKind.Stop, Stop: StopReason.ConsentWithdrawn), step);
        Assert.Equal(Phase.Stopped, m.Phase);
    }

    [Fact]
    public void Hostile_reply_during_deepening_releases_the_interviewee_and_ends_the_deepening()
    {
        var m = AtOnboarding();
        m.OnReply(Plain() with { Serious = true });

        var step = m.OnReply(Plain() with { Hostile = true });

        Assert.Equal(new Step(TurnKind.Topic, Topic.Management, Preface.AcknowledgeFrustration, Move: Move.Open), step);
        Assert.False(m.Deepening);
    }

    [Fact]
    public void A_name_during_deepening_is_redirected_and_deepening_resumes_afterwards()
    {
        var m = AtOnboarding();
        m.OnReply(Plain() with { Serious = true });

        var redirect = m.OnReply(Plain() with { NamesPerson = true });
        var resumed = m.OnReply(Plain());

        Assert.Equal(new Step(TurnKind.Redirect, Topic.Onboarding, Move: Move.Redirect), redirect);
        Assert.Equal(TurnKind.DeepProbe, resumed.Kind);
        Assert.Equal(DeepFocus.WhenHowOften, resumed.Focus);
    }

    [Fact]
    public void Deepening_never_goes_past_the_interviewer_turn_budget()
    {
        // Start, warm-up, reason for leaving: three turns. The deepening question is the fourth; the budget then closes.
        var m = AtReason(Proto.WithLimits(Limits with { MaxInterviewerTurns = 4 }));
        var deep = m.OnReply(Plain() with { Serious = true });

        var step = m.OnReply(Plain());

        Assert.Equal(TurnKind.DeepProbe, deep.Kind);
        Assert.Equal(TurnKind.Close, step.Kind);
        Assert.Equal(CloseReason.BudgetExhausted, step.Close);
    }

    [Fact]
    public void Deepening_starts_again_on_the_next_topic_but_the_reminder_is_not_repeated()
    {
        var m = AtOnboarding();
        m.OnReply(Plain() with { Serious = true });
        for (var i = 0; i < Proto.Limits.MaxDeepProbesPerTopic - 1; i++) m.OnReply(Plain());
        m.OnReply(Plain());

        var nextTopicSerious = m.OnReply(Plain() with { Serious = true });

        Assert.Equal(TurnKind.DeepProbe, nextTopicSerious.Kind);
        Assert.Equal(Topic.Management, nextTopicSerious.Topic);
        Assert.Equal(DeepFocus.WhatHappened, nextTopicSerious.Focus);
        Assert.Equal(Preface.None, nextTopicSerious.Preface);
    }

    [Fact]
    public void Without_a_serious_signal_the_machine_sequence_is_the_protocol_one()
    {
        var m = AtOnboarding();

        var kinds = new List<TurnKind> { m.OnReply(Vague()).Kind, m.OnReply(Plain()).Kind, m.OnReply(Plain()).Kind };

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
