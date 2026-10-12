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

/// <summary>An optional sentence the interviewer says before its question (spec §3.7, §3.5).</summary>
public enum Preface
{
    None,
    AcknowledgeFrustration,
    /// <summary>Said once per interview, before the first deepening question: the interviewee may skip or stop.</summary>
    DeepeningReminder,
    /// <summary>Said once, at the first topic transition after the check-in point: finish now and keep what was said.</summary>
    CheckIn,
    /// <summary>The reply said the thing was already said (spec §3.4 rule 1).</summary>
    AlreadySaid,
    /// <summary>After a distress statement (spec §3.7): the person may skip the topic or end, and the notice names help.</summary>
    Support,
    /// <summary>Said once per interview, after a legal remark (spec §3.7). The flow continues.</summary>
    LegalNotice,
}

/// <summary>
/// What the interviewer must do next. The model never chooses this; it only words the question. <see cref="Move"/> is the move behind the turn
/// (protocol 1.3); <see cref="Callback"/> is set when the reason-for-leaving or a topic was already mentioned earlier in the interview.
/// </summary>
public readonly record struct Step(TurnKind Kind, Topic? Topic = null, Preface Preface = Preface.None, CloseReason? Close = null, StopReason? Stop = null, DeepFocus? Focus = null, Move? Move = null, bool Callback = false);

/// <summary>
/// The interview as an explicit state machine with no I/O: <see cref="Start"/> and <see cref="OnReply"/> are the only transitions, each returns the
/// next <see cref="Step"/>, and every branch is covered by a unit test. A free-running model loop cannot reach <see cref="Phase.Closed"/> or
/// skip a topic; interviewee text can only influence the machine through the booleans in <see cref="ReplySignals"/> and, for the order of the
/// topics, through <see cref="Observe"/> (which skips injected sentences).
/// </summary>
/// <remarks>
/// Protocol 1.3 flow (interview dynamics spec §3.1): the warm-up, then the reason for leaving, then the other five topics in the order
/// <see cref="TopicPlanner"/> gives (core topics get the follow-up policy, light topics one scale question), then retention and anything else,
/// then close. Safety and control replies keep their priority: withdrawal, finish request, budget, hostile, name, distress, serious account,
/// already said, contradiction. Everything else follows the policy of §3.4 and the length budget of §3.5.
/// </remarks>
public sealed class InterviewMachine
{
    /// <summary>The first real topic: what the person most wants to say (spec §3.1).</summary>
    private const Topic FirstTopic = Topic.ReasonForLeaving;

    private readonly InterviewProtocol _protocol;
    private readonly ProtocolLimits _limits;
    private readonly TopicPlanner _planner;
    private readonly HashSet<Topic> _opened = [];
    private readonly HashSet<Move> _movesOnTopic = [];

    private Item _item = Item.None;
    private Topic? _topic;
    private Depth _depth = Depth.Core;
    private bool _deepening;
    private int _deepAsked;
    private bool _reminded;
    private bool _forceLightNext;
    private bool _checkedIn;
    private bool _legalDue;
    private bool _legalNoticed;
    private bool _retentionAsked;
    private bool _anythingAsked;
    private int _questions;
    private int _terseReplies;
    private int _coreOpened;

    /// <summary>
    /// The core topics of the fallback plan (no topic has salience): the first two in protocol order, fixed at the first topic transition.
    /// The planner re-ranks on every call, so this set is what keeps the other topics light.
    /// </summary>
    private HashSet<Topic>? _fallbackCore;

    public InterviewMachine(InterviewProtocol protocol)
    {
        _protocol = protocol;
        _limits = protocol.Limits;
        _planner = new TopicPlanner(protocol);
    }

    /// <summary>The position in the interview: the warm-up, a topic, retention, or anything else.</summary>
    private enum Item { None, Warmup, Topic, Retention, AnythingElse }

    public Phase Phase { get; private set; } = Phase.NotStarted;

    /// <summary>The topic of the turn last issued, or null for the warm-up and "anything else".</summary>
    public Topic? CurrentTopic => _topic;

    /// <summary>Follow-up questions (example, elaboration, ladder, consequence, scale follow-up) asked on the current topic.</summary>
    public int ProbesUsed { get; private set; }

    public int ClarificationsUsed { get; private set; }
    public int RedirectsUsed { get; private set; }
    public int DeepProbesUsed { get; private set; }
    public int ConsentAsks { get; private set; }
    public int TerseStreak { get; private set; }
    public int HostileCount { get; private set; }
    public int InterviewerTurns { get; private set; }

    /// <summary>Question turns issued so far (the warm-up, topics, follow-ups, retention, anything else, clarifications, redirects, deepening).</summary>
    public int QuestionsAsked => _questions;

    public bool IsTerminal => Phase is Phase.Closed or Phase.Stopped;

    /// <summary>True while the current topic is in its deepening phase.</summary>
    public bool Deepening => _deepening;

    /// <summary>How many topics were put to the interviewee so far (the reason for leaving counts once, though it is asked twice).</summary>
    public int TopicsAsked => _opened.Count;

    /// <summary>
    /// Adds the cue hits of one masked reply to the planner, before <see cref="OnReply"/> is called with its signals. The planner skips sentences
    /// that look like instructions, so an injected sentence cannot reorder the topics (spec §3.3, constraint C-05).
    /// </summary>
    public void Observe(string maskedReply, Topic? answering) => _planner.Observe(maskedReply, answering);

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
            case ConsentAnswer.Unclear when ConsentAsks >= _limits.MaxConsentAsks:
                return Terminate(StopReason.ConsentUnclear);
            case ConsentAnswer.Unclear:
                ConsentAsks++;
                return Issue(new Step(TurnKind.ConsentReask));
            default:
                return StartWarmup();
        }
    }

    private Step OnAnswer(ReplySignals s, bool budgetExhausted)
    {
        if (s.Withdrawal) return Terminate(StopReason.ConsentWithdrawn);

        // Asking to finish ("możemy już skończyć?") ends the dialogue politely and keeps the record: not a withdrawal, never answered with a question.
        if (s.FinishRequest) return Finish(CloseReason.Unresponsive);

        TerseStreak = s.Terse && s.Polarity == 0 && !s.Serious ? TerseStreak + 1 : 0;
        if (s.Terse) _terseReplies++;
        if (s.Hostile) HostileCount++;
        if (s.LegalThreat && !_legalNoticed) _legalDue = true;

        if (budgetExhausted || InterviewerTurns >= _limits.MaxInterviewerTurns) return Finish(CloseReason.BudgetExhausted);
        if (s.Hostile && HostileCount >= _limits.HostileToClose) return Finish(CloseReason.Hostile);
        if (TerseStreak >= _limits.TerseStreakToClose) return Finish(CloseReason.Unresponsive);

        // A frustrated interviewee is never pressed: no redirect, probe or clarification, just a kind word and the next step.
        if (s.Hostile) return Advance(Preface.AcknowledgeFrustration);

        if (s.NamesPerson && Phase != Phase.AwaitingRedirectAnswer && RedirectsUsed < _limits.MaxRedirectsPerTopic)
        {
            RedirectsUsed++;
            Phase = Phase.AwaitingRedirectAnswer;
            return Ask(TurnKind.Redirect, _topic, Move.Redirect);
        }

        if (_item == Item.AnythingElse) return Finish(CloseReason.AllTopicsCovered);

        // Distress releases the topic at once: no follow-up, no deepening, and the next topic is light (spec §3.7).
        if (s.Distress) return Advance(Preface.Support, forceLight: true);

        if (_item == Item.Retention) return WrapUp(Preface.None);

        // A serious account opens the deepening (on a topic, not the warm-up); an open deepening keeps asking until its limit, then the topic advances.
        if (_topic is { } deepTopic && (s.Serious || _deepening))
        {
            if (!_deepening) StartDeepening();
            if (DeepProbesUsed < _limits.MaxDeepProbesPerTopic && NextFocus(s.DeepCovered) is { } focus)
                return DeepProbe(deepTopic, focus);
            return Advance(Preface.None);
        }

        if (_item == Item.Warmup) return Advance(Preface.None);

        if (s.AlreadySaid) return Advance(Preface.AlreadySaid);

        if (s.Contradiction && Phase != Phase.AwaitingClarification && ClarificationsUsed < _limits.MaxClarificationsPerTopic)
        {
            ClarificationsUsed++;
            Phase = Phase.AwaitingClarification;
            return Ask(TurnKind.Clarification, _topic, Move.Clarify);
        }

        // An answer to a clarification is not followed up again.
        if (Phase == Phase.AwaitingClarification) return Advance(Preface.None);

        return _depth == Depth.Light ? LightPolicy(s) : CorePolicy(s);
    }

    /// <summary>The follow-up policy of a core topic (spec §3.4, rules 2–6). Rule 1 (already said) and the safety branches come before it.</summary>
    private Step CorePolicy(ReplySignals s)
    {
        var topic = _topic!.Value;
        var limit = topic == FirstTopic ? _limits.MaxFollowUpsReason : _limits.MaxFollowUpsCore;
        if (ProbesUsed >= limit || PastWrapUpPoint) return Advance(Preface.None);

        var canExtend = _questions < _limits.TargetQuestions;
        var substantive = s.Words >= _limits.SubstantiveWords;

        // Rule 3 and 4: a short, vague or unspecific answer gets one example request.
        if (!Used(Move.Example) && (s.Short || s.Vague || substantive && !s.Concrete)) return Follow(Move.Example);

        // Rule 5: a substantive answer with a concrete cue is followed up once, on the reason for leaving with a ladder.
        if (substantive && s.Concrete)
        {
            if (topic == FirstTopic && canExtend && !Used(Move.Ladder)) return Follow(Move.Ladder);
            if (ProbesUsed == 0 && canExtend && !Used(Move.Consequence) && !Used(Move.Elaborate))
                return Follow(s.Polarity != 0 ? Move.Elaborate : Move.Consequence);
        }

        // Rule 6: a terse answer, or one still vague after the example, advances.
        return Advance(Preface.None);
    }

    /// <summary>The follow-up policy of a light topic (spec §3.4, rule 7): one scale follow-up on an extreme value, one open-answer follow-up.</summary>
    private Step LightPolicy(ReplySignals s)
    {
        if (ProbesUsed >= _limits.MaxFollowUpsLight || PastWrapUpPoint) return Advance(Preface.None);

        if (s.ScaleValue is { } value)
        {
            return value is 1 or 2 or 5 && !Used(Move.ScaleFollowUp) ? Follow(Move.ScaleFollowUp) : Advance(Preface.None);
        }

        // A long reply with no number counts as an open answer: rule 5 applies once.
        if (s.Words >= _limits.SubstantiveWords && s.Concrete && ProbesUsed == 0 && _questions < _limits.TargetQuestions && !Used(Move.Consequence) && !Used(Move.Elaborate))
            return Follow(s.Polarity != 0 ? Move.Elaborate : Move.Consequence);

        return Advance(Preface.None);
    }

    /// <summary>At the length budget's wrap-up point no new follow-up is asked; the next topic transition then goes to retention (spec §3.5).</summary>
    private bool PastWrapUpPoint => _questions >= _limits.MaxQuestions - 2;

    private bool Used(Move move) => _movesOnTopic.Contains(move);

    private Step Follow(Move move)
    {
        _movesOnTopic.Add(move);
        ProbesUsed++;
        Phase = Phase.AwaitingProbeAnswer;
        var kind = move == Move.Deep ? TurnKind.DeepProbe : TurnKind.Probe;
        return Ask(kind, _topic, move);
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
        return Ask(TurnKind.DeepProbe, topic, Move.Deep, preface, focus);
    }

    /// <summary>What comes after the current item, when it is answered.</summary>
    private Step Advance(Preface preface, bool forceLight = false)
    {
        if (forceLight) _forceLightNext = true;
        return _item switch
        {
            Item.Warmup => OpenTopic(FirstTopic, Depth.Core, preface),
            Item.Topic => NextTopic(preface),
            Item.Retention => WrapUp(preface),
            _ => Finish(CloseReason.AllTopicsCovered),
        };
    }

    private Step StartWarmup()
    {
        _item = Item.Warmup;
        _topic = null;
        return Ask(TurnKind.Topic, null, Move.Warmup);
    }

    /// <summary>Opens a topic: a core one with an open question, a light one with the scale question (spec §3.1).</summary>
    private Step OpenTopic(Topic topic, Depth depth, Preface preface)
    {
        _opened.Add(topic);
        if (depth == Depth.Core && topic != FirstTopic) _coreOpened++;
        _item = Item.Topic;
        _topic = topic;
        _depth = depth;
        ProbesUsed = ClarificationsUsed = RedirectsUsed = 0;
        _movesOnTopic.Clear();
        _deepening = false;
        _deepAsked = 0;
        DeepProbesUsed = 0;
        var callback = depth == Depth.Core && _planner.Mentioned(topic);
        return Ask(TurnKind.Topic, topic, depth == Depth.Core ? Move.Open : Move.Scale, preface, callback: callback);
    }

    /// <summary>
    /// The next topic in the planner's order, at a topic transition. The check-in is said at the first transition after its point.
    /// Core topics are capped at <see cref="ProtocolLimits.MaxCoreTopics"/> over the interview, and none after the target question count (§3.5).
    /// </summary>
    private Step NextTopic(Preface preface)
    {
        if (PastWrapUpPoint) return WrapUp(preface);

        var targetReached = _questions >= _limits.TargetQuestions;
        var coreLeft = targetReached ? 0 : Math.Max(0, _limits.MaxCoreTopics - _coreOpened);
        var plan = _planner.Plan(_opened, coreLeft);
        if (plan.Count == 0) return WrapUp(preface);

        var (topic, depth) = plan[0];
        var salient = Enum.GetValues<Topic>().Any(t => !_opened.Contains(t) && _planner.Salience(t) > 0);
        if (!salient)
        {
            _fallbackCore ??= plan.Take(2).Select(p => p.Topic).ToHashSet();
            depth = _fallbackCore.Contains(topic) ? Depth.Core : Depth.Light;
        }
        if (targetReached || _forceLightNext) depth = Depth.Light;
        _forceLightNext = false;
        if (preface == Preface.None && CheckInDue)
        {
            _checkedIn = true;
            preface = Preface.CheckIn;
        }
        return OpenTopic(topic, depth, preface);
    }

    /// <summary>After the check-in point, once, unless the person was terse twice (spec §3.5).</summary>
    private bool CheckInDue => !_checkedIn && _questions >= _limits.CheckInAfterQuestions && _terseReplies < 2;

    /// <summary>Retention, then anything else, then the close (spec §3.1). Used at the end and at the length budget's wrap-up point.</summary>
    private Step WrapUp(Preface preface)
    {
        if (!_retentionAsked)
        {
            _retentionAsked = true;
            _item = Item.Retention;
            _topic = FirstTopic;
            _movesOnTopic.Clear();
            return Ask(TurnKind.Topic, FirstTopic, Move.Retention, preface);
        }

        if (!_anythingAsked)
        {
            _anythingAsked = true;
            _item = Item.AnythingElse;
            _topic = null;
            return Ask(TurnKind.Topic, null, Move.AnythingElse, preface);
        }

        return Finish(CloseReason.AllTopicsCovered);
    }

    /// <summary>Issues a question turn. The first preface is kept; the legal notice waits for a turn that has none.</summary>
    private Step Ask(TurnKind kind, Topic? topic, Move move, Preface preface = Preface.None, DeepFocus? focus = null, bool callback = false)
    {
        if (_legalDue && !_legalNoticed && preface == Preface.None)
        {
            _legalDue = false;
            _legalNoticed = true;
            preface = Preface.LegalNotice;
        }
        Phase = kind == TurnKind.Topic ? Phase.AwaitingAnswer : Phase;
        _questions++;
        return Issue(new Step(kind, topic, preface, Focus: focus, Move: move, Callback: callback));
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
