namespace ExitInterviewAgent.Providers;

public enum ProviderKind
{
    /// <summary>The Anthropic API with the user's own API key.</summary>
    Anthropic,
    /// <summary>Any endpoint that speaks the OpenAI chat-completions API: a hosted service or a local gateway.</summary>
    OpenAiCompatible,
    /// <summary>A local (or LAN) Ollama server. No key.</summary>
    Ollama,
    /// <summary>The scripted offline model: a test seam, not a provider. Never leaves the machine.</summary>
    Mock,
}

/// <summary>One row of the provider matrix. <see cref="KeyEnvVar"/> is the default variable the key is read from; a key is never read from a flag.</summary>
public sealed record ProviderInfo(
    ProviderKind Kind,
    string Id,
    string DisplayName,
    string? DefaultBaseUrl,
    string? KeyEnvVar,
    bool KeyRequired,
    string? BaseUrlEnvVar,
    string Summary);

public static class ProviderCatalog
{
    public const string ReadmeAnchor = "README.md#run-it-with-your-own-model";

    public static IReadOnlyList<ProviderInfo> All { get; } =
    [
        new(ProviderKind.Anthropic, "anthropic", "Anthropic API", "https://api.anthropic.com", "ANTHROPIC_API_KEY", true, null,
            "The Anthropic API with your own API key (official .NET SDK)."),
        new(ProviderKind.OpenAiCompatible, "openai-compatible", "OpenAI-compatible endpoint", "https://api.openai.com/v1", "OPENAI_API_KEY", false, "OPENAI_BASE_URL",
            "Any endpoint that speaks the OpenAI chat-completions API: base URL plus key, or a local gateway without a key."),
        new(ProviderKind.Ollama, "ollama", "Ollama", "http://localhost:11434", null, false, "OLLAMA_HOST",
            "A local Ollama server, native API, no key. Your transcript stays on this machine when the server is local."),
        new(ProviderKind.Mock, "mock", "Scripted mock model", null, null, false, null,
            "Offline test seam: deterministic, understands nothing, not a quality baseline. No network."),
    ];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"] = "openai-compatible",
        ["openai_compatible"] = "openai-compatible",
        ["claude"] = "anthropic",
    };

    /// <summary>Ids that name a backend this project deliberately does not offer, with the reason shown to the user.</summary>
    private static readonly Dictionary<string, string> Refused = new(StringComparer.OrdinalIgnoreCase)
    {
        ["copilot"] = CopilotReason,
        ["github-copilot"] = CopilotReason,
        ["githubcopilot"] = CopilotReason,
        ["github_copilot"] = CopilotReason,
        ["claude-subscription"] = SubscriptionReason,
        ["claude-ai"] = SubscriptionReason,
        ["claude-code"] = SubscriptionReason,
        ["claude-pro"] = SubscriptionReason,
        ["claude-max"] = SubscriptionReason,
        ["claude-oauth"] = SubscriptionReason,
        ["subscription"] = SubscriptionReason,
    };

    public const string CopilotReason =
        "GitHub Copilot is not supported as a model backend. This is a decision, not a finding that it is prohibited: the terms for using Copilot "
        + "credentials from a separate program could not be verified, so no adapter exists. Use an API key (--provider anthropic or openai-compatible) or a local model (--provider ollama). See "
        + ReadmeAnchor + ".";

    public const string SubscriptionReason =
        "Claude subscription (Free, Pro or Max) credentials are not supported. Anthropic's documentation says third-party developers may not offer Claude.ai login or route requests "
        + "through Free, Pro or Max plan credentials. Use an Anthropic API key (--provider anthropic) or a local model (--provider ollama). See " + ReadmeAnchor + ".";

    /// <summary>Resolves a provider id (case-insensitive, with the aliases <c>openai</c> and <c>claude</c>). Unsupported backends throw <see cref="UnsupportedProviderException"/>; unknown ids throw <see cref="ProviderConfigurationException"/>.</summary>
    public static ProviderInfo Get(string id)
    {
        var key = id.Trim();
        if (Refused.TryGetValue(key, out var reason)) throw new UnsupportedProviderException(reason);
        if (Aliases.TryGetValue(key, out var canonical)) key = canonical;
        return All.FirstOrDefault(p => string.Equals(p.Id, key, StringComparison.OrdinalIgnoreCase))
            ?? throw new ProviderConfigurationException($"Unknown provider (not echoed: it may have been a secret pasted into the wrong place). Supported: {string.Join(", ", All.Where(p => p.Kind != ProviderKind.Mock).Select(p => p.Id))}, and 'mock' for the offline test model.");
    }

    public static ProviderInfo Get(ProviderKind kind) => All.Single(p => p.Kind == kind);
}
