using ExitInterviewAgent.ServiceDefaults;
using ExitInterviewAgent.Signals.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ExitInterviewAgent.Signals;

/// <summary>P4 for the module's own context: migrate, never ensure (InMemory is the one path that creates the model directly).</summary>
public sealed class SignalsMigrationService(
    IServiceScopeFactory scopes, SignalsSchemaSignal signal, DatabaseMode mode, ILogger<SignalsMigrationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SignalsDbContext>();
            if (mode.IsRelational)
            {
                await db.Database.MigrateAsync(stoppingToken);
            }
            else
            {
                await db.Database.EnsureCreatedAsync(stoppingToken);
            }
            signal.Complete();
            logger.LogInformation("signals schema ready");
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            signal.Fail(ex);
            logger.LogError("signals schema migration failed ({ErrorType})", ex.GetType().Name);
        }
    }
}
