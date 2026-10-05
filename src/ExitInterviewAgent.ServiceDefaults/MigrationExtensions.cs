using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ExitInterviewAgent.ServiceDefaults;

/// <summary>Completes when schema work is done. Other hosted services await it (SERVICE-API-PATTERNS §7).</summary>
public sealed class MigrationCompletionSignal
{
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsCompleted => _done.Task.IsCompleted;
    public Exception? Failure { get; private set; }
    public Task WaitAsync(CancellationToken ct) => _done.Task.WaitAsync(ct);
    public void Complete() => _done.TrySetResult();
    public void Fail(Exception ex) { Failure = ex; _done.TrySetResult(); }
}

public static class MigrationExtensions
{
    /// <summary>
    /// Applies <c>MigrateAsync</c> from a hosted service AFTER Kestrel starts (P4), so probes answer while
    /// schema work is in flight. InMemory is the one path that may <c>EnsureCreated</c>. <c>/health</c> is
    /// unhealthy until the schema is ready.
    /// </summary>
    public static IServiceCollection AddMigrationOnStartup<TContext>(this IServiceCollection services) where TContext : DbContext
    {
        services.AddSingleton<MigrationCompletionSignal>();
        services.AddHostedService<MigrationBackgroundService<TContext>>();
        services.AddHealthChecks().AddCheck<SchemaReadyHealthCheck>("schema");
        return services;
    }

    private sealed class MigrationBackgroundService<TContext>(
        IServiceScopeFactory scopes, MigrationCompletionSignal signal, DatabaseMode mode,
        ILogger<MigrationBackgroundService<TContext>> logger) : BackgroundService where TContext : DbContext
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Yield so the host finishes starting (Kestrel listening) before any schema work.
            await Task.Yield();
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TContext>();
                if (mode.IsRelational)
                {
                    await db.Database.MigrateAsync(stoppingToken);
                }
                else
                {
                    await db.Database.EnsureCreatedAsync(stoppingToken); // InMemory/test path only
                }
                signal.Complete();
                logger.LogInformation("schema ready");
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                signal.Fail(ex);
                logger.LogError(ex, "schema migration failed");
            }
        }
    }

    private sealed class SchemaReadyHealthCheck(MigrationCompletionSignal signal) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
            => Task.FromResult(!signal.IsCompleted ? HealthCheckResult.Unhealthy("schema migration in progress")
                : signal.Failure is null ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("schema migration failed"));
    }
}
