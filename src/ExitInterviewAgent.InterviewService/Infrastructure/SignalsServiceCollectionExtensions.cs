using ExitInterviewAgent.InterviewService.Signals;
using ExitInterviewAgent.Signals;

namespace ExitInterviewAgent.InterviewService.Infrastructure;

/// <summary>Wires the Signals module into this service: the module's own registrations plus the one transport adapter (ADR-0052).</summary>
public static class SignalsHostExtensions
{
    public static IServiceCollection AddSignalsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSignals(configuration);
        services.AddScoped<IObservationSource, RecordStoreObservationSource>();
        return services;
    }
}
