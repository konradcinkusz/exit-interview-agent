using ExitInterviewAgent.InterviewService.Billing;
using ExitInterviewAgent.InterviewService.Interviews;
using ExitInterviewAgent.Providers;
using ExitInterviewAgent.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Dev;

/// <summary>
/// Development-only conveniences (ADR-0079): the dev credit grant, and the visibility of the interview model in <c>/health</c>
/// and the startup banner. The grant is mounted only when the environment is Development AND payments are the fake provider AND
/// checkout is configured; in every other environment the route does not exist (404). Production refuses the fake provider
/// (ADR-0077), so the rule holds twice.
/// </summary>
public static class DevStack
{
    /// <summary>The one rule for the dev credit grant: Development, the fake provider, and a complete checkout configuration.</summary>
    public static bool CreditGrantEnabled(IHostEnvironment environment, BillingOptions billing) =>
        environment.IsDevelopment() && billing.Provider == "fake" && billing.Configured(out _);

    /// <summary>
    /// Registers two rows: <c>interview-model</c> (degraded while the scripted mock runs, so a mock is never mistaken for a real
    /// interview) and <c>dev-credit-grant</c>. The grant row reports configured when the grant is off by design: an absent dev route
    /// is the correct state outside Development, not a degradation. Both are evaluated on first use, when the environment is known.
    /// </summary>
    public static IServiceCollection AddDevStack(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IntegrationStatus>(_ => ModelStatus(configuration));
        services.AddSingleton<IntegrationStatus>(sp => GrantStatus(
            sp.GetRequiredService<IHostEnvironment>(),
            sp.GetRequiredService<IOptions<BillingOptions>>().Value));
        return services;
    }

    private static IntegrationStatus ModelStatus(IConfiguration configuration)
    {
        var options = configuration.GetSection(InterviewServiceOptions.SectionName).Get<InterviewServiceOptions>() ?? new InterviewServiceOptions();
        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            return new IntegrationStatus("interview-model", false, "no interview provider configured: interviews answer 503");
        }

        try
        {
            // The same resolver and the same environment as ConfiguredInterviewModelFactory, so this row cannot disagree with the model that runs.
            var resolved = ProviderConfigResolver.Resolve(
                new ProviderCliOptions { Provider = options.Provider, Model = options.Model, BaseUrl = options.BaseUrl, ApiKeyEnv = options.ApiKeyEnv },
                Environment.GetEnvironmentVariable);
            return resolved.Settings.Kind == ProviderKind.Mock
                ? new IntegrationStatus("interview-model", false,
                    "scripted MOCK model: the interview is scripted, not generated; no real model answers. Set the anthropic-api-key parameter to use a real provider")
                : new IntegrationStatus("interview-model", true, $"provider '{options.Provider}' is configured; the key is never reported");
        }
        catch (ProviderConfigurationException e)
        {
            // The resolver's message names variables and missing settings, never a value.
            return new IntegrationStatus("interview-model", false, $"no usable interview model: {e.Message}");
        }
    }

    private static IntegrationStatus GrantStatus(IHostEnvironment environment, BillingOptions billing) =>
        CreditGrantEnabled(environment, billing)
            ? new IntegrationStatus("dev-credit-grant", true, "POST /api/v1/dev/grant-credits is mounted (Development, fake payments): credits granted here are NOT purchases")
            : new IntegrationStatus("dev-credit-grant", true, "not mounted, by design: it exists only in Development with the fake payment provider");
}
