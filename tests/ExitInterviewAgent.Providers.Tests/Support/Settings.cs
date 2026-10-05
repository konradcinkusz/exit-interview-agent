namespace ExitInterviewAgent.Providers.Tests.Support;

public static class Settings
{
    public const string Key = "sk-test-KEY-0123456789";

    public static ProviderSettings For(ProviderKind kind, ResilienceOptions? resilience = null, string baseUrl = "") => new()
    {
        Kind = kind,
        Model = kind switch { ProviderKind.Anthropic => "claude-test-1", ProviderKind.OpenAiCompatible => "gpt-test-1", _ => "llama-test:1b" },
        BaseUrl = new Uri(baseUrl.Length > 0 ? baseUrl : kind switch { ProviderKind.Anthropic => "https://api.example.test", ProviderKind.OpenAiCompatible => "https://gateway.example.test/v1", _ => "http://localhost:11434" }),
        ApiKey = kind == ProviderKind.Ollama ? null : new SecretString(Key),
        Resilience = resilience ?? new ResilienceOptions(),
    };

    public static readonly ProviderKind[] RealKinds = [ProviderKind.Anthropic, ProviderKind.OpenAiCompatible, ProviderKind.Ollama];

    public static IEnumerable<object[]> Kinds() => RealKinds.Select(k => new object[] { k });
}
