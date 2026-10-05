using System.Threading.RateLimiting;
using ExitInterviewAgent.ServiceDefaults;
using ExitInterviewAgent.Signals.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using OpenTelemetry.Metrics;

namespace ExitInterviewAgent.Signals;

public static class SignalsRateLimit
{
    public const string Policy = "signals";
}

public static class SignalsServiceCollectionExtensions
{
    /// <summary>The module keeps its own migration history, inside its own schema.</summary>
    public static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql)
        => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", SignalsDbContext.Schema);

    /// <summary>
    /// Registers the module. The host must also register an <see cref="IObservationSource"/> (scoped): that registration is the
    /// transport, and the only place the module meets the rest of the service.
    /// </summary>
    public static IServiceCollection AddSignals(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(SignalsOptions.SectionName);
        var settings = section.Get<SignalsOptions>() ?? new SignalsOptions();
        services.AddOptions<SignalsOptions>().Bind(section)
            .ValidateDataAnnotations()
            .Validate(o => !o.Problems().Any(), "Signals configuration is invalid: Signals:PublishInterval is a whole number of hours between 1 hour and 7 days; Signals:CheckInterval is between 1 second and the interval.")
            .ValidateOnStart();

        // A private InMemory name per registration: two hosts in one process never share a store.
        services.AddDatabaseContext<SignalsDbContext>(configuration, "interviewdb", "SignalsInMemory-" + Guid.NewGuid().ToString("N"), ConfigureNpgsql, reportIntegration: false);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SignalsSchemaSignal>();
        services.AddSingleton<PublicationState>();
        services.AddSingleton<SignalsMetrics>();
        services.ConfigureOpenTelemetryMeterProvider(m => m.AddMeter(SignalsMetrics.MeterName));
        services.AddScoped<SnapshotPublisher>();
        services.AddScoped<SnapshotReader>();
        services.AddHostedService<SignalsMigrationService>();
        services.AddHostedService<SnapshotPublisherService>();
        services.AddHealthChecks().AddCheck<SignalsHealthCheck>("signals", HealthStatus.Degraded);

        services.AddIntegration("signals", settings.Enabled, settings.Enabled
            ? $"snapshots every {(int)settings.PublishInterval.TotalHours} h (never per submission), k = {settings.MinimumGroupSize}, rules v{DisclosureRules.Version}; deletions appear at the next snapshot"
            : "publisher disabled (Signals:Enabled = false): the last snapshot, if any, is still served");

        services.AddRateLimiter(limiter =>
        {
            limiter.AddPolicy(SignalsRateLimit.Policy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.RequestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });
        return services;
    }
}
