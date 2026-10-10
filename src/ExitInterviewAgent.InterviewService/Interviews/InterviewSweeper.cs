namespace ExitInterviewAgent.InterviewService.Interviews;

/// <summary>Wipes idle and finished sessions every 30 seconds. Lookups also expire sessions, so this only bounds memory.</summary>
public sealed class InterviewSweeper(InterviewSessionStore store, InterviewSessions sessions) : BackgroundService
{
    internal static readonly TimeSpan Period = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            store.Sweep();
            // The sessions wiped just now (and any whose settlement failed before) are settled here, so their credit state is written (W11).
            await sessions.SettleEndedAsync().ConfigureAwait(false);
        }
    }
}
