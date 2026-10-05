using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Providers.Tests;

/// <summary>Skipped unless the named environment variable holds a key. Never run in CI (no secret is configured there); the model id comes from <c>EXIT_INTERVIEW_LIVE_MODEL</c>, because this project hardcodes none.</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute(string keyVariable)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(keyVariable))) Skip = $"live: {keyVariable} is not set";
        else if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("EXIT_INTERVIEW_LIVE_MODEL"))) Skip = "live: EXIT_INTERVIEW_LIVE_MODEL is not set";
    }
}

[Trait("Category", "Live")]
public class LiveSmokeTests
{
    private static async Task Ping(string provider)
    {
        var resolved = ProviderConfigResolver.Resolve(
            new ProviderCliOptions { Provider = provider, Model = Environment.GetEnvironmentVariable("EXIT_INTERVIEW_LIVE_MODEL") }, Environment.GetEnvironmentVariable);
        using var client = ProviderChatClients.Create(resolved.Settings);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "Reply with the single word OK.")], new ChatOptions { MaxOutputTokens = 8, Temperature = 0 });

        Assert.False(string.IsNullOrWhiteSpace(response.Text));
        Assert.True(client.Budget.Snapshot().InputTokens > 0);
    }

    [LiveFact("ANTHROPIC_API_KEY")]
    public Task Anthropic_answers_a_minimal_request() => Ping("anthropic");

    [LiveFact("OPENAI_API_KEY")]
    public Task An_openai_compatible_endpoint_answers_a_minimal_request() => Ping("openai-compatible");
}
