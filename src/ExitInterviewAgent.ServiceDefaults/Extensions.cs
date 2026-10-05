using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace ExitInterviewAgent.ServiceDefaults;

/// <summary>Telemetry, health, discovery and resilience: opt in with <c>AddServiceDefaults()</c> (P2, P2a).</summary>
public static class Extensions
{
    private const string LivenessTag = "live";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), [LivenessTag]);
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = false; // scopes can carry request data; keep PII out of telemetry
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                // Probe noise would otherwise dominate the traces (P15).
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx =>
                    !ctx.Request.Path.StartsWithSegments("/health") && !ctx.Request.Path.StartsWithSegments("/alive"))
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }
        builder.Services.AddIntegration("telemetry-export",
            !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]),
            "OTLP traces/metrics/logs when OTEL_EXPORTER_OTLP_ENDPOINT is set; otherwise in-process only");
        return builder;
    }

    /// <summary><c>/health</c> (readiness, everything, with the integration list) and <c>/alive</c> (liveness only).</summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = WriteHealthAsync })
            .DisableRateLimiting();
        app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = r => r.Tags.Contains(LivenessTag) })
            .DisableRateLimiting();
        return app;
    }

    private static Task WriteHealthAsync(HttpContext context, HealthReport report)
    {
        var integrations = context.RequestServices.GetServices<IntegrationStatus>().ToArray();
        var degraded = integrations.Any(i => !i.Configured);
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status != HealthStatus.Healthy ? report.Status.ToString() : degraded ? "Degraded" : "Healthy",
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
            integrations = integrations.Select(i => new { name = i.Name, configured = i.Configured, detail = i.Detail }),
        }, JsonSerializerOptions.Web));
    }
}
