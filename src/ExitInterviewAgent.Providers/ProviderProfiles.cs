using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Providers;

/// <summary>
/// The entry point for a harness profile (T7's <c>evals/profiles.yaml</c>): a provider id, a model, an optional endpoint and the values of the
/// environment variables the profile requires (each one a key variable, at most one per profile) in, a metered, budgeted, instrumented
/// <see cref="IChatClient"/> out. Every rule of the CLI path applies: the base-URL policy, the refusal of subscription-token variables, the
/// transport policy, the hard budget, error messages without content. A profile never reads a key from anywhere but the variable it names.
/// </summary>
public static class ProviderProfiles
{
    public static ProviderChatClient Create(string providerId, string? model, string? endpoint, IReadOnlyDictionary<string, string> requiredEnv, ProviderRuntime? runtime = null)
    {
        var info = ProviderCatalog.Get(providerId);
        if (info.Kind == ProviderKind.Mock) throw new ProviderConfigurationException("The mock is built into the harness and is not a provider profile.");
        if (requiredEnv.Count > 1) throw new ProviderConfigurationException("A provider profile may require at most one key variable.");

        SecretString? key = null;
        if (requiredEnv.Count == 1)
        {
            var (variable, value) = requiredEnv.Single();
            CredentialPolicy.EnsureAllowedKeySource(variable);
            key = new SecretString(value);
        }

        var settings = new ProviderSettings
        {
            Kind = info.Kind,
            Model = model ?? throw new ProviderConfigurationException("The profile has no model (set the environment variable it names)."),
            BaseUrl = BaseUrlPolicy.Validate(string.IsNullOrWhiteSpace(endpoint) ? info.DefaultBaseUrl : endpoint, keyed: key is not null),
            ApiKey = key,
        };
        return ProviderChatClients.Create(settings, runtime);
    }
}
