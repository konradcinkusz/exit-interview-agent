using System.Net;
using System.Text;
using System.Text.Json;
using ExitInterviewAgent.Agent.Mock;
using ExitInterviewAgent.Eval.Execution;
using ExitInterviewAgent.Eval.Tests.Support;
using ExitInterviewAgent.Providers;
using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Eval.Tests;

/// <summary>A profile wired to the real provider adapters, with a fake HTTP backend instead of a network (ADR-0032).</summary>
public class ProviderProfileTests
{
    /// <summary>An Ollama-shaped fake server answering with the offline scripted model, so a whole scenario runs through the real adapter.</summary>
    private sealed class FakeOllama : HttpMessageHandler
    {
        public List<(Uri Uri, Dictionary<string, string> Headers)> Seen { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen.Add((request.RequestUri!, request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase)));
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var messages = doc.RootElement.GetProperty("messages").EnumerateArray()
                .Select(m => new ChatMessage(m.GetProperty("role").GetString() == "system" ? ChatRole.System : ChatRole.User, m.GetProperty("content").GetString()!)).ToList();
            var reply = await new ScriptedChatClient().GetResponseAsync(messages, null, cancellationToken);
            var json = JsonSerializer.Serialize(new
            {
                model = "fake-model",
                message = new { role = "assistant", content = reply.Text },
                done = true,
                done_reason = "stop",
                prompt_eval_count = reply.Usage!.InputTokenCount,
                eval_count = reply.Usage.OutputTokenCount,
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static string Yaml(string name, string provider, string requires) => $"""
        profiles:
          - name: {name}
            description: test
            provider: {name}
            model_env: T_MODEL
            endpoint_env: T_ENDPOINT
            requires_env: [{requires}]
        """.Replace($"provider: {name}", $"provider: {provider}");

    private static string Write(string yaml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"profiles-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, yaml);
        return path;
    }

    private static Func<string, string?> Env(params (string, string)[] pairs)
    {
        var d = pairs.ToDictionary(p => p.Item1, p => p.Item2);
        return k => d.GetValueOrDefault(k);
    }

    [Fact]
    public async Task A_provider_profile_runs_one_scenario_end_to_end_through_the_real_adapter_against_a_fake_backend()
    {
        var backend = new FakeOllama();
        ProviderRegistration.Register("ollama-under-test", "ollama", new ProviderRuntime { Transport = backend });
        var profile = ProfileCatalog.LoadAll(Write(Yaml("ollama-under-test", "ollama-under-test", "")), Env(("T_MODEL", "some-local-model"), ("T_ENDPOINT", "http://localhost:11434")))
            .Single(p => p.Name == "ollama-under-test");

        var outcome = await ScenarioRunner.RunAsync(Fixtures.Scenario("hap-001"), 1, profile);

        Assert.Null(outcome.Error);
        Assert.True(outcome.Run!.Result.Submittable);
        Assert.Equal("ollama-under-test", outcome.Run.Profile);
        Assert.NotEmpty(backend.Seen);
        Assert.All(backend.Seen, s => Assert.EndsWith("/api/chat", s.Uri.AbsolutePath));
        Assert.All(backend.Seen, s => Assert.DoesNotContain("Authorization", s.Headers.Keys));
    }

    [Fact]
    public async Task A_keyed_profile_sends_only_the_key_from_the_variable_it_names()
    {
        var backend = new FakeOllama();
        ProviderRegistration.Register("keyed-under-test", "ollama", new ProviderRuntime { Transport = backend });
        var profile = ProfileCatalog.LoadAll(Write(Yaml("keyed-under-test", "keyed-under-test", "T_KEY")), Env(("T_MODEL", "m"), ("T_ENDPOINT", "https://gpu.example.test"), ("T_KEY", "profile-key-value-123")))
            .Single(p => p.Name == "keyed-under-test");

        var outcome = await ScenarioRunner.RunAsync(Fixtures.Scenario("hap-001"), 1, profile);

        Assert.Null(outcome.Error);
        Assert.All(backend.Seen, s => Assert.Equal("Bearer profile-key-value-123", s.Headers["Authorization"]));
    }

    [Fact]
    public void Registered_profiles_without_their_environment_still_report_skipped_no_credential_never_a_pass()
    {
        ProviderRegistration.RegisterAll();

        var profiles = ProfileCatalog.LoadAll(env: Env());

        foreach (var name in ProviderRegistration.ProviderIds)
        {
            var p = profiles.Single(x => x.Name == name);
            Assert.False(p.Runnable);
            Assert.StartsWith("skipped:no-credential", p.SkipReason);
        }
        Assert.Contains("anthropic", ProviderFactories.Registered);
        Assert.Contains("openai-compatible", ProviderFactories.Registered);
        Assert.Contains("ollama", ProviderFactories.Registered);
    }

    [Fact]
    public void A_profile_that_requires_a_subscription_token_variable_is_refused_when_the_client_is_built()
    {
        ProviderRegistration.Register("sub-under-test", "anthropic");
        var profile = ProfileCatalog.LoadAll(Write(Yaml("sub-under-test", "sub-under-test", "CLAUDE_CODE_OAUTH_TOKEN")), Env(("T_MODEL", "m"), ("CLAUDE_CODE_OAUTH_TOKEN", "token-value-xyz")))
            .Single(p => p.Name == "sub-under-test");

        var ex = Assert.Throws<UnsupportedCredentialException>(() => profile.Factory!());

        Assert.DoesNotContain("token-value-xyz", ex.Message);
        Assert.Contains("README.md#run-it-with-your-own-model", ex.Message);
    }

    [Fact]
    public void A_profile_with_an_insecure_keyed_endpoint_is_refused()
    {
        ProviderRegistration.Register("http-under-test", "openai-compatible");
        var profile = ProfileCatalog.LoadAll(Write(Yaml("http-under-test", "http-under-test", "T_KEY")), Env(("T_MODEL", "m"), ("T_ENDPOINT", "http://gateway.example.test/v1"), ("T_KEY", "k-value-123")))
            .Single(p => p.Name == "http-under-test");

        Assert.Throws<ProviderConfigurationException>(() => profile.Factory!());
    }
}
