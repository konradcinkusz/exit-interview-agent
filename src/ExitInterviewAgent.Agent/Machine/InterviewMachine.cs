using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.Agent.Machine;

public enum Phase
{
    NotStarted,
    AwaitingConsent,
    AwaitingAnswer,
    AwaitingProbeAnswer,
    AwaitingClarification,
    AwaitingRedirectAnswer,
    /// <summary>A deepening question on a serious account is out; the next reply is read as part of it.</summary>
    AwaitingDeepAnswer,
    /// <summary>The dialogue ended with consent intact; a record is prepared from what was said.</summary>
    Closed,
    /// <summary>The dialogue ended without a record.</summary>
    Stopped,
}

/// <summary>An optional sentence the interviewer says before its question.</summary>
public enum Preface
{
    None,
    AcknowledgeFrustration,
    /// <summary>Said once per interview, before the first deepening question: the interviewee may skip or stop.</summary>
    DeepeningReminder,
}

/// <summary>What the interviewer must do next. The model never chooses this; it only words the question.</summary>
public readonly record struct Step(TurnKind Kind, Topic? Topic = null, Preface Preface = Preface.None, CloseReason? Close = null, StopReason? Stop = null, DeepFocus? Focus = null);

/// <summary>
/// The interview as an explicit state machine with no I/O: <see cref="Start"/> and <see cref="OnReply"/> are the only
/// transitions, each returns the next <see cref="Step"/>, and every branch is covered by a unit test. A free-running
/// model loop cannot reach <see cref="Phase.Closed"/> or skip a topic; interviewee text can only influence the
/// machine through the booleans in <see cref="ReplySignals"/>.
/// </summary>
/// <remarks>
/// Priority of a reply on a topic, highest first (Y2): withdrawal; the budget and the closing counts (hostile, terse);
/// hostility, which is acknowledged and released; a name, which is redirected to a role; a serious account or an open
/// deepening, which asks the next uncovered menu element until <c>MaxDeepProbesPerTopic</c> and then advances (a deepening
/// does not also clarify or probe); a contradiction, which is clarified once; a vague or very short answer that says something, which gets one follow-up (a short answer is asked "what exactly", never closed on);
/// otherwise the topic advances.
/// </remarks>
public sealed class InterviewMachine
{
    private readonly InterviewProtocol _protocol;
    private int _topicIndex = -1;
    private bool _deepening;
    private int _deepAsked;
    private bool _reminded;

    public InterviewMachine(InterviewProtocol protocol) => _protocol = protocol;

    public Phase Phase { get; private set; } = Phase.NotStarted;
    public Topic? CurrentTopic => _topicIndex >= 0 && _topicIndex < _protocol.Topics.Count ? _protocol.Topics[_topicIndex].Topic : null;
    public int ProbesUsed { get; private set; }
    public int ClarificationsUsed { get; private set; }
    public int RedirectsUsed { get; private set; }
    public int DeepProbesUsed { get; private set; }
    public int ConsentAsks { get; private set; }
    public int TerseStreak { get; private set; }
    public int HostileCount { get; private set; }
    public int InterviewerTurns { get; private set; }
    public bool IsTerminal => Phase is Phase.Closed or Phase.Stopped;

    /// <summary>True while the current topic is in its deepening phase.</summary>
    public bool Deepening => _deepening;

    /// <summary>How many topics were put to the interviewee so far.</summary>
    public int TopicsAsked => Math.Min(_topicIndex + 1, _protocol.Topics.Count);

    public Step Start()
    {
        if (Phase != Phase.NotStarted) throw new InvalidOperationException("The interview has already started.");
        Phase = Phase.AwaitingConsent;
        ConsentAsks = 1;
        return Issue(new Step(TurnKind.Opening));
    }

    /// <summary>The interviewee left without a word (connection closed, input ended). No record: nothing was agreed to.</summary>
    public Step OnDisconnect() => Terminate(StopReason.Disconnected);

    public Step OnReply(ReplySignals s, bool budgetExhausted = false)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (Phase is Phase.NotStarted) throw new InvalidOperationException("Start the interview first.");
        if (IsTerminal) throw new InvalidOperationException("The interview has ended.");

        return Phase == Phase.AwaitingConsent ? OnConsentReply(s) : OnAnswer(s, budgetExhausted);
    }

    private Step OnConsentReply(ReplySignals s)
    {
        if (s.Withdrawal) return Terminate(StopReason.ConsentWithdrawn);
        switch (s.Consent)
        {
            case ConsentAnswer.No:
                return Terminate(StopReason.ConsentDeclined);
            case ConsentAnswer.Unclear when ConsentAsks >= _protocol.Limits.MaxConsentAsks:
                return Terminate(StopReason.ConsentUnclear);
            case ConsentAnswer.Unclear:
                ConsentAsks++;
                return Issue(new Step(TurnKind.ConsentReask));
            default:
                return Advance(Preface.None);
        }
    }

    private Step OnAnswer(ReplySignals s, bool budgetExhausted)
    {
        var limits = _protocol.Limits;
        if (s.Withdrawal) return Terminate(StopReason.ConsentWithdrawn);

        // A short reply that still says something ("słabe", "zwolnili mnie") is an answer, not a refusal to talk: only bare non-answers build the streak.
        // Asking to finish ("możemy już skończyć?") ends the dialogue politely and keeps the record: it is not a withdrawal and is never answered with another question.
        if (s.FinishRequest) return Finish(CloseReason.Unresponsive);

        TerseStreak = s.Terse && s.Polarity == 0 && !s.Serious ? TerseStreak + 1 : 0;
        if (s.Hostile) HostileCount++;

        if (budgetExhausted || InterviewerTurns >= limits.MaxInterviewerTurns) return Finish(CloseReason.BudgetExhausted);
        if (s.Hostile && HostileCount >= limits.HostileToClose) return Finish(CloseReason.Hostile);
        if (TerseStreak >= limits.TerseStreakToClose) return Finish(CloseReason.Unresponsive);

        // A frustrated interviewee is never pressed: no redirect, probe or clarification, just a kind word and the next topic.
        if (s.Hostile) return Advance(Preface.AcknowledgeFrustration);

        var topic = CurrentTopic!.Value;

        if (s.NamesPerson && Phase != Phase.AwaitingRedirectAnswer && RedirectsUsed < limits.MaxRedirectsPerTopic)
        {
            RedirectsUsed++;
            Phase = Phase.AwaitingRedirectAnswer;
            return Issue(new Step(TurnKind.Redirect, topic));
        }

        // A serious account opens the deepening; an open deepening keeps asking until its limit, then the topic advances.
        if (s.Serious || _deepening)
        {
            if (!_deepening) StartDeepening();
            if (DeepProbesUsed < limits.MaxDeepProbesPerTopic && NextFocus(s.DeepCovered) is { } focus)
                return DeepProbe(topic, focus);
            return Advance(Preface.None);
        }

        if (s.Contradiction && Phase != Phase.AwaitingClarification && ClarificationsUsed < limits.MaxClarificationsPerTopic)
        {
            ClarificationsUsed++;
            Phase = Phase.AwaitingClarification;
            return Issue(new Step(TurnKind.Clarification, topic));
        }

        if ((s.Vague || s.Short) && ProbesUsed < limits.MaxProbesPerTopic && Phase != Phase.AwaitingClarification)
        {
            ProbesUsed++;
            Phase = Phase.AwaitingProbeAnswer;
            return Issue(new Step(TurnKind.Probe, topic));
        }

        return Advance(Preface.None);
    }

    private void StartDeepening()
    {
        _deepening = true;
        _deepAsked = 0;
        DeepProbesUsed = 0;
    }

    /// <summary>The first menu element neither asked before nor described by the reply just given (the menu order is the protocol's).</summary>
    private DeepFocus? NextFocus(int covered)
    {
        foreach (var focus in Enum.GetValues<DeepFocus>())
            if (((_deepAsked | covered) & (1 << (int)focus)) == 0) return focus;
        return null;
    }

    private Step DeepProbe(Topic topic, DeepFocus focus)
    {
        DeepProbesUsed++;
        _deepAsked |= 1 << (int)focus;
        Phase = Phase.AwaitingDeepAnswer;
        var preface = _reminded ? Preface.None : Preface.DeepeningReminder;
        _reminded = true;
        return Issue(new Step(TurnKind.DeepProbe, topic, preface, Focus: focus));
    }

    private Step Advance(Preface preface)
    {
        _topicIndex++;
        ProbesUsed = ClarificationsUsed = RedirectsUsed = 0;
        _deepening = false;
        _deepAsked = 0;
        DeepProbesUsed = 0;
        if (_topicIndex >= _protocol.Topics.Count) return Finish(CloseReason.AllTopicsCovered);
        Phase = Phase.AwaitingAnswer;
        return Issue(new Step(TurnKind.Topic, CurrentTopic, preface));
    }

    private Step Finish(CloseReason reason)
    {
        Phase = Phase.Closed;
        return Issue(new Step(TurnKind.Close, Close: reason));
    }

    private Step Terminate(StopReason reason)
    {
        if (IsTerminal) throw new InvalidOperationException("The interview has ended.");
        Phase = Phase.Stopped;
        return Issue(new Step(TurnKind.Stop, Stop: reason));
    }

    private Step Issue(Step step)
    {
        InterviewerTurns++;
        return step;
    }
}
