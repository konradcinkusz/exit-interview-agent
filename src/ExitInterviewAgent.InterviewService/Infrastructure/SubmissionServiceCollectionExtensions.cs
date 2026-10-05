using ExitInterviewAgent.InterviewService.Submissions;
using ExitInterviewAgent.Records;
using ExitInterviewAgent.ServiceDefaults;
using OpenTelemetry.Metrics;

namespace ExitInterviewAgent.InterviewService.Infrastructure;

/// <summary>Wiring for submission, receipts, tickets, the ledger and retention. Kept out of the kernel: this is domain code (P2).</summary>
public static class SubmissionServiceCollectionExtensions
{
    public static IServiceCollection AddSubmissions(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.Configure<SubmissionOptions>(configuration.GetSection(SubmissionOptions.SectionName));
        var submission = configuration.GetSection(SubmissionOptions.SectionName).Get<SubmissionOptions>() ?? new SubmissionOptions();

        // Eager on purpose: outside Development a missing or malformed ledger key stops the service at startup.
        var keys = LedgerKeySet.Create(configuration.GetSection(LedgerOptions.SectionName).Get<LedgerOptions>() ?? new LedgerOptions(), environment.IsDevelopment());
        services.AddSingleton(keys);
        services.AddIntegration("ledger-key", !keys.IsEphemeral, keys.IsEphemeral
            ? "no Ledger:Keys configured: an EPHEMERAL development key was generated for this process (the key is not logged); ledger entries do not survive a restart"
            : $"{keys.KeyIds.Count} key(s), active key id '{keys.ActiveKeyId}'");

        // Ingest policy: the AI-disclosure check runs after the PII re-scan (pipeline order), so the library's own check is off here.
        services.AddSingleton(new RecordValidator(new RecordLimits { RequireAiDisclosed = false }));
        services.AddSingleton<IPiiScanner, PiiScanner>();
        services.AddSingleton<IEmploymentVerifier, MockEmploymentVerifier>();
        services.AddIntegration("employment-verifier", configured: false,
            $"mock verifier answering '{submission.Verification.MockMode}': no real employment verification exists (OP-1); an unavailable verifier degrades submissions to level 'unchecked'");

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<StoreGate>();
        services.AddSingleton<SubmissionMetrics>();
        services.ConfigureOpenTelemetryMeterProvider(m => m.AddMeter(SubmissionMetrics.MeterName));
        services.AddScoped<TicketStore>();
        services.AddScoped<SubmissionService>();
        services.AddScoped<ReceiptService>();
        services.AddScoped<RetentionPurger>();
        services.AddHostedService<RetentionService>();
        services.AddSubmissionRateLimits();
        return services;
    }
}
