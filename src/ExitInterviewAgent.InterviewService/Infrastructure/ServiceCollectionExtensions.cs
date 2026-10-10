using ExitInterviewAgent.InterviewService.Billing.Endpoints;
using ExitInterviewAgent.InterviewService.Endpoints;
using ExitInterviewAgent.InterviewService.Infrastructure.Auth;
using ExitInterviewAgent.InterviewService.Interviews;
using ExitInterviewAgent.InterviewService.Interviews.CostControls;
using ExitInterviewAgent.InterviewService.Interviews.Endpoints;
using ExitInterviewAgent.InterviewService.Mcp;
using ExitInterviewAgent.InterviewService.Persistence;
using ExitInterviewAgent.ServiceDefaults;
using OpenTelemetry.Metrics;

namespace ExitInterviewAgent.InterviewService.Infrastructure;

/// <summary>This service's own wiring; Program.cs only lists capabilities.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInterviewPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDatabaseContext<InterviewDbContext>(configuration, "interviewdb", "InterviewInMemory");
        services.AddMigrationOnStartup<InterviewDbContext>();
        return services;
    }

    /// <summary>
    /// The cost controls on the session routes (web-app-plan §2, ADR-0078): the per-account and per-address limits, the
    /// verified-email gate, the global daily cap and the spend metrics. Registered after the sessions so the sessions' emergency
    /// switch and credit seams are already in place. The kernel's rate-limit policies are not changed.
    /// </summary>
    public static IServiceCollection AddInterviewCostControls(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CostControlOptions>().Bind(configuration.GetSection(InterviewServiceOptions.SectionName));
        services.AddSingleton<CostMetrics>();
        services.AddSingleton<InterviewRateLimits>();
        services.AddSingleton<DailyStartCap>();
        services.ConfigureOpenTelemetryMeterProvider(m => m.AddMeter(CostMetrics.MeterName));

        // Optional and visible (P8): /health and the startup banner show the cost-control state. No key, no environment variable name.
        var section = configuration.GetSection(InterviewServiceOptions.SectionName);
        var sessions = section.Get<InterviewServiceOptions>() ?? new InterviewServiceOptions();
        var costs = section.Get<CostControlOptions>() ?? new CostControlOptions();
        var providerConfigured = !string.IsNullOrWhiteSpace(sessions.Provider);
        services.AddIntegration("interview-cost-controls", sessions.Enabled && providerConfigured,
            $"emergency switch {(sessions.Enabled ? "on (enabled)" : "off (disabled)")}; provider configured: {(providerConfigured ? "yes" : "no")}; "
            + $"verified email required: {(costs.RequireVerifiedEmail ? "yes" : "no")}; daily start cap: {costs.MaxStartsPerDay} (UTC day); "
            + "per-account and per-address limits on starts and replies");
        return services;
    }

    public static WebApplication UseInterviewPipeline(this WebApplication app)
    {
        app.UseMcpTransportGuard();
        app.UseCors(CorsPolicies.Frontend);
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }
        return app;
    }

    /// <summary>The authorization triad (SERVICE-API-PATTERNS §2): public and authenticated groups, visible here.</summary>
    public static WebApplication MapInterviewEndpoints(this WebApplication app)
    {
        var authApi = app.MapGroup("/api/v1").RequireAuthorization(AuthPolicies.Account).RequireRateLimiting(ApiExtensions.ApiPolicy).WithValidation();
        authApi.MapAccountEndpoints();
        authApi.MapAccountSubmissionEndpoints();
        authApi.MapSignalsEndpoints();
        authApi.MapInterviewSessionEndpoints();
        authApi.MapCreditEndpoints();

        // No account, no token: ticketed submission, receipt deletion and the payment webhook. Short enough to read aloud; the architecture test pins the list.
        var publicApi = app.MapGroup("/api/v1").AllowAnonymous().WithValidation();
        publicApi.MapAnonymousSubmissionEndpoints();
        publicApi.MapPaymentWebhookEndpoint();

        // The MCP resource server: public RFC 9728 metadata, and the mount point guarded by the MCP policy (ADR-0012).
        var mcp = app.Services.GetRequiredService<McpOptions>();
        app.MapProtectedResourceMetadata(mcp);
        app.MapMcpMount(mcp);
        return app;
    }
}
