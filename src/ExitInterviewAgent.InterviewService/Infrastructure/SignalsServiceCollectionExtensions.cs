using ExitInterviewAgent.InterviewService.Signals;
using ExitInterviewAgent.ServiceDefaults;
using ExitInterviewAgent.Signals;

namespace ExitInterviewAgent.InterviewService.Infrastructure;

/// <summary>Wires the Signals module into this service: the module's own registrations, the one transport adapter (ADR-0052) and the optional demo data.</summary>
public static class SignalsHostExtensions
{
    public static IServiceCollection AddSignalsModule(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSignals(configuration);
        services.AddScoped<IObservationSource, RecordStoreObservationSource>();

        services.Configure<DemoDataOptions>(configuration.GetSection(DemoDataOptions.SectionName));
        var demo = configuration.GetSection(DemoDataOptions.SectionName).Get<DemoDataOptions>() ?? new DemoDataOptions();
        if (demo.IsActive)
        {
            // Synthetic records in a store that real people use would be indistinguishable from fabricated reviews: refuse to start.
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException($"{DemoDataOptions.SectionName}:Mode is only honoured in Development; unset it.");
            }
            services.AddSingleton<DemoDataService>();
            services.AddHostedService(sp => sp.GetRequiredService<DemoDataService>());
            // Visible on /health and in the startup banner, and "not configured" on purpose: demo data is a degraded state to be seen.
            services.AddIntegration("signals-demo-data", configured: false,
                $"DEMO DATA ACTIVE (mode {demo.Mode}, seed {demo.Seed}): synthetic records under the '{DemoRecords.Prefix}' prefix, Development only; remove with {DemoDataOptions.SectionName}:Mode=Remove");
        }
        return services;
    }
}
