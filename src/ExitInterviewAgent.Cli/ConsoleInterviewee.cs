using ExitInterviewAgent.Agent.Runner;

namespace ExitInterviewAgent.Cli;

/// <summary>
/// A person at a terminal. <see cref="ReplyAsync"/> shows the interviewer's words and reads one line. End of input (Ctrl-D,
/// a closed pipe) and cancellation (Ctrl-C) both return <c>null</c>, which the runner treats as the interviewee leaving: the
/// transcript is discarded and no record is built. Nothing is echoed to disk, the transcript stays in the runner's memory.
/// </summary>
public sealed class ConsoleInterviewee(TextReader input, TextWriter output) : IInterviewee
{
    public async Task<string?> ReplyAsync(IntervieweeTurn turn, CancellationToken ct)
    {
        await Show(turn).ConfigureAwait(false);
        await output.WriteAsync("> ").ConfigureAwait(false);
        await output.FlushAsync(ct).ConfigureAwait(false);
        try
        {
            var line = await ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) await output.WriteLineAsync().ConfigureAwait(false);
            return line;
        }
        catch (OperationCanceledException)
        {
            await output.WriteLineAsync().ConfigureAwait(false);
            return null;
        }
    }

    public Task DeliverAsync(IntervieweeTurn turn, CancellationToken ct) => Show(turn);

    private async Task Show(IntervieweeTurn turn)
    {
        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteLineAsync("Interviewer: " + turn.Text).ConfigureAwait(false);
        await output.WriteLineAsync().ConfigureAwait(false);
    }

    /// <summary>A console read cannot be cancelled, so the read is abandoned (the process is ending anyway) when the token fires.</summary>
    private async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var read = input.ReadLineAsync();
        if (read.IsCompleted) return await read.ConfigureAwait(false);
        var cancelled = new TaskCompletionSource();
        await using var registration = ct.Register(() => cancelled.TrySetResult());
        var winner = await Task.WhenAny(read, cancelled.Task).ConfigureAwait(false);
        if (winner != read) throw new OperationCanceledException(ct);
        return await read.ConfigureAwait(false);
    }
}
