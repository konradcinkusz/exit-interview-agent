using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ExitInterviewAgent.ServiceDefaults;

/// <summary>
/// One optional integration and whether it is live (P8). Registered at startup, reported by
/// <c>/health</c> and printed in the startup banner so "what is degraded?" is one request.
/// </summary>
public sealed record IntegrationStatus(string Name, bool Configured, string Detail);

public static class IntegrationExtensions
{
    public static IServiceCollection AddIntegration(
        this IServiceCollection services, string name, bool configured, string detail)
        => services.AddSingleton(new IntegrationStatus(name, configured, detail));

    /// <summary>Logs the same list <c>/health</c> returns. Call once after the app is built.</summary>
    public static void LogIntegrationBanner(this IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        foreach (var integration in services.GetServices<IntegrationStatus>())
        {
            logger.LogInformation("integration {Name}: {State} ({Detail})",
                integration.Name, integration.Configured ? "configured" : "DEGRADED", integration.Detail);
        }
    }
}
