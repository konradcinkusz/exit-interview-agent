using ExitInterviewAgent.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.Signals;

/// <summary>
/// Looks at the clock every <see cref="SignalsOptions.CheckInterval"/> and runs the publisher, which decides whether a batch is due.
/// Waits for both schemas (the interview store it reads and its own) before the first run
/// (SERVICE-API-PATTERNS section 7). The publisher itself is what tests drive with a fake clock.
/// </summary>
public sealed class SnapshotPublisherService(
    IServiceScopeFactory scopes,
    MigrationCompletionSignal interviewSchema,
    SignalsSchemaSignal signalsSchema,
    IOptions<SignalsOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }
        await interviewSchema.WaitAsync(stoppingToken);
        await signalsSchema.WaitAsync(stoppingToken);
        if (interviewSchema.Failure is not null || signalsSchema.Failure is not null)
        {
            return;
        }
        using var timer = new PeriodicTimer(options.Value.CheckInterval);
        do
        {
            using var scope = scopes.CreateScope();
            // Failures are handled and reported inside the publisher: a failed run must not stop the loop.
            await scope.ServiceProvider.GetRequiredService<SnapshotPublisher>().RunDueAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
