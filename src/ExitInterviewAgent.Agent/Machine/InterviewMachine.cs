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
    /// <summary>The dialogue ended with consent intact; a record is prepared from what was said.</summary>
    Closed,
    /// <summary>The dialogue ended without a record.</summary>
    Stopped,
}

/// <summary>An optional sentence the interviewer says before its question.</summary>
public enum Preface { None, AcknowledgeFrustration }

/// <summary>What the interviewer must do next. The model never chooses this; it only words the question.</summary>
public readonly record struct Step(TurnKind Kind, Topic? Topic = null, Preface Preface = Preface.None, CloseReason? Close = null, StopReason? Stop = null);

/// <summary>
/// The interview as an explicit state machine with no I/O: <see cref="Start"/> and <see cref="OnReply"/> are the only
/// transitions, each returns the next <see cref="Step"/>, and every branch is covered by a unit test. A free-running
/// model loop cannot reach <see cref="Phase.Closed"/> or skip a topic; interviewee text can only influence the
/// machine through the booleans in <see cref="ReplySignals"/>.
/// </summary>
public sealed class InterviewMachine
{
    private readonly InterviewProtocol _protocol;
    private int _topicIndex = -1;

    public InterviewMachine(InterviewProtocol protocol) => _protocol = protocol;

    public Phase Phase { get; private set; } = Phase.NotStarted;
    public Topic? CurrentTopic => _topicIndex >= 0 && _topicIndex < _protocol.Topics.Count ? _protocol.Topics[_topicIndex].Topic : null;
    public int ProbesUsed { get; private set; }
    public int ClarificationsUsed { get; private set; }
    public int RedirectsUsed { get; private set; }
    public int ConsentAsks { get; private set; }
    public int TerseStreak { get; private set; }
    public int HostileCount { get; private set; }
    public int InterviewerTurns { get; private set; }
    public bool IsTerminal => Phase is Phase.Closed or Phase.Stopped;

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

        TerseStreak = s.Terse ? TerseStreak + 1 : 0;
        if (s.Hostile) HostileCount++;

        if (budgetExhausted || InterviewerTurns >= limits.MaxInterviewerTurns) return Finish(CloseReason.BudgetExhausted);
        if (s.Hostile && HostileCount >= limits.HostileToClose) return Finish(CloseReason.Hostile);
        if (TerseStreak >= limits.TerseStreakToClose) return Finish(CloseReason.Unresponsive);

        var topic = CurrentTopic!.Value;

        if (s.NamesPerson && Phase != Phase.AwaitingRedirectAnswer && RedirectsUsed < limits.MaxRedirectsPerTopic)
        {
            RedirectsUsed++;
            Phase = Phase.AwaitingRedirectAnswer;
            return Issue(new Step(TurnKind.Redirect, topic));
        }

        if (s.Contradiction && Phase != Phase.AwaitingClarification && ClarificationsUsed < limits.MaxClarificationsPerTopic)
        {
            ClarificationsUsed++;
            Phase = Phase.AwaitingClarification;
            return Issue(new Step(TurnKind.Clarification, topic));
        }

        if (s.Vague && ProbesUsed < limits.MaxProbesPerTopic && Phase is Phase.AwaitingAnswer or Phase.AwaitingRedirectAnswer)
        {
            ProbesUsed++;
            Phase = Phase.AwaitingProbeAnswer;
            return Issue(new Step(TurnKind.Probe, topic));
        }

        return Advance(s.Hostile ? Preface.AcknowledgeFrustration : Preface.None);
    }

    private Step Advance(Preface preface)
    {
        _topicIndex++;
        ProbesUsed = ClarificationsUsed = RedirectsUsed = 0;
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
