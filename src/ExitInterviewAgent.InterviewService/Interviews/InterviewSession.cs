using System.Threading.Channels;
using ExitInterviewAgent.Agent.Protocol;
using ExitInterviewAgent.Agent.Runner;
using ExitInterviewAgent.Contracts;
using ExitInterviewAgent.Records;

namespace ExitInterviewAgent.InterviewService.Interviews;

/// <summary>Where a session is. The four terminal states are the contract's <c>completed</c>, <c>stopped</c> and <c>failed</c>.</summary>
public enum SessionStatus { AwaitingConsent, InProgress, Completed, Stopped, Failed }

/// <summary>
/// What the request side waits for: either the next interviewer turn (<see cref="Turn"/> set) or the end of the session
/// (<see cref="Turn"/> null, <see cref="Status"/> terminal, <see cref="Ending"/> and <see cref="FinalTurn"/> set).
/// </summary>
public sealed record SessionEvent(InterviewTurn? Turn, SessionStatus Status, InterviewEnding? Ending, InterviewTurn? FinalTurn);

/// <summary>
/// One interview in server memory: the transcript-side state, the channels that connect the background interview to the
/// requests, and the finished result. Holds no text outside the interview itself; the text passes through the turn events
/// and is dropped with the session.
/// </summary>
public sealed class InterviewSession(string id, string owner, string language, TenureBand tenure, DateTimeOffset now)
{
    private readonly object _gate = new();

    // One reply at a time: the interviewee takes one reply per turn (bounded to 1), and the endpoint refuses a second while one is pending.
    private readonly Channel<string> _replies = Channel.CreateBounded<string>(new BoundedChannelOptions(1) { SingleReader = true, SingleWriter = true });
    private readonly Channel<SessionEvent> _events = Channel.CreateUnbounded<SessionEvent>(new UnboundedChannelOptions { SingleReader = true });

    public string Id { get; } = id;
    public string Owner { get; } = owner;
    public string Language { get; } = language;
    public TenureBand Tenure { get; } = tenure;
    public CancellationTokenSource Cancellation { get; } = new();

    public SessionStatus Status { get; private set; } = SessionStatus.AwaitingConsent;
    public int TurnCount { get; private set; }
    public DateTimeOffset LastAccess { get; private set; } = now;
    public DateTimeOffset? FinishedAt { get; private set; }
    public InterviewResultResponse? Result { get; private set; }
    public InterviewTurn? FinalTurn { get; private set; }
    public bool AwaitingReply { get; private set; }
    public bool FinalDelivered { get; private set; }

    public bool IsTerminal => Status is SessionStatus.Completed or SessionStatus.Stopped or SessionStatus.Failed;

    /// <summary>Idle sessions expire <see cref="InterviewServiceOptions.IdleTimeout"/> after their last request; finished ones <see cref="InterviewServiceOptions.ResultTtl"/> after they finished.</summary>
    public DateTimeOffset ExpiresAt(InterviewServiceOptions options) =>
        IsTerminal && FinishedAt is { } finished ? finished + options.ResultTtl : LastAccess + options.IdleTimeout;

    public bool IsExpired(DateTimeOffset now, InterviewServiceOptions options) => now >= ExpiresAt(options);

    public void Touch(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!IsTerminal) LastAccess = now;
        }
    }

    /// <summary>The interviewee shows a turn that expects a reply. Called on the interview's task, before it waits for the reply.</summary>
    public void PublishTurn(TurnKind kind, string text)
    {
        lock (_gate)
        {
            if (IsTerminal) return;
            var index = TurnCount++;
            Status = kind is TurnKind.Opening or TurnKind.ConsentReask ? SessionStatus.AwaitingConsent : SessionStatus.InProgress;
            AwaitingReply = true;
            _events.Writer.TryWrite(new SessionEvent(new InterviewTurn(index, Snake(kind), text), Status, null, null));
        }
    }

    /// <summary>
    /// The closing (or stop) turn, which expects no reply. It is shown at once, while the status is still in progress: the tiles
    /// are generated after it, and the reply does not wait for them. The status becomes terminal with the end event.
    /// </summary>
    public void DeliverFinalTurn(TurnKind kind, string text)
    {
        lock (_gate)
        {
            if (IsTerminal) return;
            FinalTurn = new InterviewTurn(TurnCount++, Snake(kind), text);
            FinalDelivered = true;
            _events.Writer.TryWrite(new SessionEvent(FinalTurn, Status, null, null));
        }
    }

    /// <summary>The first half of a reply: refuses it when the session has ended or no turn is waiting for one.</summary>
    public string? BeginReply()
    {
        lock (_gate)
        {
            if (IsTerminal || FinalDelivered) return InterviewCodes.InterviewEnded;
            if (!AwaitingReply) return InterviewCodes.ReplyInProgress;
            AwaitingReply = false;
            return null;
        }
    }

    /// <summary>Passes a reply to the interviewee. Only valid after <see cref="BeginReply"/> returned null.</summary>
    public bool TrySendReply(string text) => _replies.Writer.TryWrite(text);

    /// <summary>The interviewee's side of the reply channel.</summary>
    public ValueTask<string> ReadReplyAsync(CancellationToken ct) => _replies.Reader.ReadAsync(ct);

    /// <summary>Marks the session ended. Returns false when it had already ended (the first ending wins).</summary>
    public bool Finish(SessionStatus status, string reason, InterviewResultResponse? result, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (IsTerminal) return false;
            Status = status;
            FinishedAt = now;
            Result = result;
            AwaitingReply = false;
            _events.Writer.TryWrite(new SessionEvent(null, status, new InterviewEnding(reason), FinalTurn));
            return true;
        }
    }

    /// <summary>Waits for the next event, or null when <paramref name="timeout"/> passes first. A cancelled request is not a timeout.</summary>
    public async Task<SessionEvent?> NextEventAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        try
        {
            return await _events.Reader.ReadAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    public void Cancel() => Cancellation.Cancel();

    public InterviewState ToState(InterviewServiceOptions options) =>
        new(Id, ToSnake(Status.ToString()), Language, TurnCount, ExpiresAt(options));

    internal static string Snake(TurnKind kind) => ToSnake(kind.ToString());

    internal static string ToSnake(string pascal) =>
        string.Concat(pascal.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}
