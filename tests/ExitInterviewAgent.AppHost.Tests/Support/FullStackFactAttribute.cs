namespace ExitInterviewAgent.AppHost.Tests.Support;

/// <summary>
/// A test that starts the whole AppHost: PostgreSQL, pgAdmin and authservice as containers, interview-service and the built web
/// app as processes. It is SKIPPED, with the reason, unless <c>FULL_STACK_TESTS=1</c> is set and a Docker daemon is reachable.
/// A skip is reported as NOT RUN; it is never a pass (the same rule as <c>PostgresFactAttribute</c>).
/// </summary>
public sealed class FullStackFactAttribute : FactAttribute
{
    public const string EnabledVariable = "FULL_STACK_TESTS";
    public const string DockerSocket = "/var/run/docker.sock";

    public FullStackFactAttribute()
    {
        var reason = ReasonToSkip(
            Environment.GetEnvironmentVariable(EnabledVariable),
            Environment.GetEnvironmentVariable("DOCKER_HOST") is { Length: > 0 } || File.Exists(DockerSocket));
        if (reason is not null) Skip = reason;
    }

    /// <summary>The skip reason, or null when the full stack may run. Pure, so the gate itself is unit-tested.</summary>
    public static string? ReasonToSkip(string? enabled, bool dockerReachable)
    {
        if (enabled != "1") return $"NOT RUN: {EnabledVariable}=1 is not set (the full-stack CI job sets it).";
        if (!dockerReachable) return $"NOT RUN: no Docker daemon ({DockerSocket} and DOCKER_HOST are absent); the stack needs the PostgreSQL, pgAdmin and authservice containers.";
        return null;
    }
}
