using ExitInterviewAgent.InterviewService.Interviews;
using ExitInterviewAgent.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Infrastructure;

/// <summary>The web interview sessions (ADR-0076): in memory, one background sweeper, and the seams W3 replaces.</summary>
public static class InterviewSessionServiceCollectionExtensions
{
    public static IServiceCollection AddInterviewSessions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InterviewServiceOptions>().Bind(configuration.GetSection(InterviewServiceOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<InterviewSessionStore>();
        services.AddSingleton<IInterviewModelFactory, ConfiguredInterviewModelFactory>();
        services.AddSingleton<InterviewSessions>();
        services.AddSingleton<ICreditRefund, NoopCreditRefund>();
        services.AddSingleton<ICreditGate>(sp => sp.GetRequiredService<IOptions<InterviewServiceOptions>>().Value.RequireCredit
            ? new RefuseAllCreditGate()
            : new AllowAllCreditGate());
        services.AddHostedService<InterviewSweeper>();

        // Optional and visible (P8): /health and the startup banner say whether interviews can start, and why not.
        var options = configuration.GetSection(InterviewServiceOptions.SectionName).Get<InterviewServiceOptions>() ?? new InterviewServiceOptions();
        var configured = options.Enabled && !string.IsNullOrWhiteSpace(options.Provider);
        services.AddIntegration("interviews", configured,
            !options.Enabled ? "disabled by Interviews:Enabled (emergency switch): new sessions answer 503"
            : configured ? $"provider '{options.Provider}': new sessions use it; credits {(options.RequireCredit ? "required (no ledger yet: every start is refused)" : "not required (W3 adds them)")}"
            : "no Interviews:Provider configured: new sessions answer 503");
        return services;
    }
}
