using ExitInterviewAgent.Agent.Tracing;
using ExitInterviewAgent.Providers;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ExitInterviewAgent.Cli;

/// <summary>
/// OpenTelemetry export for the CLI: <b>off unless asked</b>, through the standard variables. Traces are exported when
/// <c>OTEL_TRACES_EXPORTER</c> names <c>otlp</c> and/or <c>console</c>, or, when it is unset, when an OTLP endpoint is configured
/// (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c> or <c>OTEL_EXPORTER_OTLP_TRACES_ENDPOINT</c>); metrics likewise with <c>OTEL_METRICS_EXPORTER</c>.
/// <c>OTEL_SDK_DISABLED=true</c> turns everything off. The OTLP exporter reads the rest (<c>OTEL_EXPORTER_OTLP_PROTOCOL</c>, headers) itself.
/// The console exporter is buffered and printed once when the interview ends, so it does not interleave with the dialogue.
/// Only the two project sources are registered: the HTTP stack's and the SDKs' own telemetry is never exported.
/// There is no switch to record prompts or completions (<see cref="ProviderTelemetry"/>).
/// </summary>
public static class TelemetrySetup
{
    public static readonly IReadOnlyList<string> Sources = [InterviewTelemetry.ActivitySourceName, ProviderTelemetry.SourceName];

    public static readonly IReadOnlyList<string> Meters = [ProviderTelemetry.MeterName];

    private static readonly TimeSpan Never = TimeSpan.FromHours(1);

    /// <summary>The exporters selected for one signal: a subset of <c>otlp</c> and <c>console</c>. Unknown names are ignored (and reported by <see cref="Unknown"/>).</summary>
    public static IReadOnlyList<string> Exporters(Func<string, string?> env, string selectorVariable, params string[] endpointVariables)
    {
        if (string.Equals(env("OTEL_SDK_DISABLED"), "true", StringComparison.OrdinalIgnoreCase)) return [];
        var selector = env(selectorVariable);
        if (!string.IsNullOrWhiteSpace(selector))
            return selector.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => s.ToLowerInvariant()).Where(s => s is "otlp" or "console").Distinct().ToList();
        return endpointVariables.Any(v => !string.IsNullOrWhiteSpace(env(v))) ? ["otlp"] : [];
    }

    public static IReadOnlyList<string> Unknown(Func<string, string?> env, string selectorVariable) =>
        (env(selectorVariable) ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => s.ToLowerInvariant()).Where(s => s is not ("otlp" or "console" or "none")).ToList();

    public static IReadOnlyList<string> TraceExporters(Func<string, string?> env) =>
        Exporters(env, "OTEL_TRACES_EXPORTER", "OTEL_EXPORTER_OTLP_ENDPOINT", "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT");

    public static IReadOnlyList<string> MetricExporters(Func<string, string?> env) =>
        Exporters(env, "OTEL_METRICS_EXPORTER", "OTEL_EXPORTER_OTLP_ENDPOINT", "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT");

    /// <summary>Starts the providers that the environment asks for. Dispose to flush and stop them. When nothing is asked for, nothing is built.</summary>
    public static IDisposable Start(Func<string, string?> env)
    {
        var traces = TraceExporters(env);
        var metrics = MetricExporters(env);
        var resource = ResourceBuilder.CreateDefault().AddService(env("OTEL_SERVICE_NAME") is { Length: > 0 } name ? name : "exit-interview", serviceVersion: InterviewTelemetry.Version);
        var owned = new List<IDisposable>();

        if (traces.Count > 0)
        {
            var builder = Sdk.CreateTracerProviderBuilder().SetResourceBuilder(resource).AddSource([.. Sources]);
            if (traces.Contains("otlp")) builder.AddOtlpExporter();
            if (traces.Contains("console"))
                builder.AddProcessor(new BatchActivityExportProcessor(new OpenTelemetry.Exporter.ConsoleActivityExporter(new OpenTelemetry.Exporter.ConsoleExporterOptions()), scheduledDelayMilliseconds: (int)Never.TotalMilliseconds));
            owned.Add(builder.Build());
        }

        if (metrics.Count > 0)
        {
            var builder = Sdk.CreateMeterProviderBuilder().SetResourceBuilder(resource).AddMeter([.. Meters]);
            if (metrics.Contains("otlp")) builder.AddOtlpExporter();
            if (metrics.Contains("console")) builder.AddConsoleExporter((_, reader) => reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = (int)Never.TotalMilliseconds);
            owned.Add(builder.Build());
        }

        return new Composite(owned);
    }

    private sealed class Composite(List<IDisposable> items) : IDisposable
    {
        public void Dispose()
        {
            foreach (var i in items) i.Dispose();
        }
    }
}
