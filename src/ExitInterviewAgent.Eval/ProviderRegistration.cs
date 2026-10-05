using ExitInterviewAgent.Eval.Execution;
using ExitInterviewAgent.Providers;

namespace ExitInterviewAgent.Eval;

/// <summary>
/// The one place the harness meets the Providers project (ADR-0032): it registers a factory per provider name, and nothing else in the
/// harness constructs or knows a provider. A profile's key comes only from the variable it lists in <c>requires_env</c>; the model and endpoint
/// from the variables it names. No model id and no secret is committed.
/// </summary>
public static class ProviderRegistration
{
    public static readonly IReadOnlyList<string> ProviderIds = ["anthropic", "openai-compatible", "ollama"];

    /// <summary>Registers the three real providers under their own names. <paramref name="runtime"/> replaces transport, clock and jitter (tests pass a fake).</summary>
    public static void RegisterAll(ProviderRuntime? runtime = null)
    {
        foreach (var id in ProviderIds) Register(id, id, runtime);
    }

    /// <summary>Registers <paramref name="providerId"/> under <paramref name="name"/> (a test registers under a private name).</summary>
    public static void Register(string name, string providerId, ProviderRuntime? runtime = null) =>
        ProviderFactories.Register(name, s => ProviderProfiles.Create(providerId, s.Model, s.Endpoint, s.Env, runtime));
}
