using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ExitInterviewAgent.Signals;

/// <summary>
/// Readiness and degradation of the module (P8). Unhealthy until its schema exists; Degraded when the last publication failed or the
/// snapshot being served is more than two batches old (the previous snapshot keeps being served, and says how old it is);
/// never a hard failure of the service for a stale aggregate.
/// </summary>
public sealed class SignalsHealthCheck(
    SignalsSchemaSignal schema, SnapshotReader reader, PublicationState state, IOptions<SignalsOptions> options, TimeProvider time) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (!schema.IsCompleted)
        {
            return HealthCheckResult.Unhealthy("signals schema migration in progress");
        }
        if (schema.Failure is not null)
        {
            return HealthCheckResult.Unhealthy("signals schema migration failed");
        }
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return HealthCheckResult.Healthy("publisher disabled; serving the last snapshot, if any");
        }
        var current = await reader.CurrentAsync(ct);
        if (current is null)
        {
            return state.LastRunFailed
                ? HealthCheckResult.Degraded("no snapshot yet and the last publication failed")
                : HealthCheckResult.Healthy("awaiting the first publication");
        }
        if (time.GetUtcNow() - current.GeneratedAt > 2 * settings.PublishInterval + settings.CheckInterval)
        {
            return HealthCheckResult.Degraded("the snapshot being served is more than two batches old");
        }
        return state.LastRunFailed
            ? HealthCheckResult.Degraded("the last publication failed; the previous snapshot is still served")
            : HealthCheckResult.Healthy();
    }
}
