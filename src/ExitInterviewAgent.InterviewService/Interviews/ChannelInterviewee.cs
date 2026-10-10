using ExitInterviewAgent.Agent.Runner;

namespace ExitInterviewAgent.InterviewService.Interviews;

/// <summary>
/// The interviewee of a web session: each interviewer turn is published to the session, and the interview waits on the reply
/// channel until a request brings the next reply. Cancelling the session (delete, expiry) ends the wait with
/// <see cref="OperationCanceledException"/>, which the conductor treats as a silent end.
/// </summary>
internal sealed class ChannelInterviewee(InterviewSession session) : IInterviewee
{
    public async Task<string?> ReplyAsync(IntervieweeTurn turn, CancellationToken ct)
    {
        session.PublishTurn(turn.Kind, turn.Text);
        return await session.ReadReplyAsync(ct).ConfigureAwait(false);
    }

    public Task DeliverAsync(IntervieweeTurn turn, CancellationToken ct)
    {
        session.DeliverFinalTurn(turn.Kind, turn.Text);
        return Task.CompletedTask;
    }
}
