using ExitInterviewAgent.InterviewService.Billing;
using ExitInterviewAgent.InterviewService.Interviews;
using ExitInterviewAgent.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.InterviewService.Infrastructure;

/// <summary>The web interview sessions (ADR-0076): in memory, one background sweeper, and the credit seams (ledger when required, ADR-0077).</summary>
public static class InterviewSessionServiceCollectionExtensions
{
    public static IServiceCollection AddInterviewSessions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InterviewServiceOptions>().Bind(configuration.GetSection(InterviewServiceOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<InterviewSessionStore>();
        services.AddSingleton<IInterviewModelFactory, ConfiguredInterviewModelFactory>();
        services.AddSingleton<InterviewSessions>();
        // Interviews:RequireCredit (ADR-0077): true in production, where a start takes one credit from the ledger; false in development and tests.
        services.AddSingleton<ICreditSettlement>(sp => RequiresCredit(sp) ? sp.GetRequiredService<LedgerCreditSettlement>() : new NoopCreditSettlement());
        services.AddSingleton<ICreditGate>(sp => RequiresCredit(sp) ? sp.GetRequiredService<LedgerCreditGate>() : new AllowAllCreditGate());
        services.AddHostedService<InterviewSweeper>();
        // The startup sweep (W11): one instance is also the one that runs it, so the test can await the same run the host made.
        services.AddSingleton<LostSessionSweep>();
        services.AddHostedService(sp => sp.GetRequiredService<LostSessionSweep>());

        // Optional and visible (P8): /health and the startup banner say whether interviews can start, and why not.
        var options = configuration.GetSection(InterviewServiceOptions.SectionName).Get<InterviewServiceOptions>() ?? new InterviewServiceOptions();
        var configured = options.Enabled && !string.IsNullOrWhiteSpace(options.Provider);
        services.AddIntegration("interviews", configured,
            !options.Enabled ? "disabled by Interviews:Enabled (emergency switch): new sessions answer 503"
            : configured ? $"provider '{options.Provider}': new sessions use it; credits {(options.RequireCredit ? "required (the ledger: one credit per start; a failed or lost session gets its credit back)" : "not required (development: starts are free)")}"
            : "no Interviews:Provider configured: new sessions answer 503");
        return services;
    }

    private static bool RequiresCredit(IServiceProvider sp) => sp.GetRequiredService<IOptions<InterviewServiceOptions>>().Value.RequireCredit;
}
