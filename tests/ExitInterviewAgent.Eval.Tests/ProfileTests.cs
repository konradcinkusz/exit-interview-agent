using ExitInterviewAgent.Eval.Execution;
using ExitInterviewAgent.Eval.Scenarios;
using ExitInterviewAgent.Eval.Tests.Support;
using Microsoft.Extensions.AI;

namespace ExitInterviewAgent.Eval.Tests;

public class ProfileTests
{
    private static string Yaml(string provider) => $"""
        profiles:
          - name: p-test
            description: a test profile
            provider: {provider}
            model_env: P_MODEL
            endpoint_env: P_ENDPOINT
            requires_env: [P_KEY]
        judge:
          name: j-test
          description: a test judge
          provider: {provider}
          model_env: J_MODEL
          requires_env: [P_KEY]
        """;

    private static string Write(string yaml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"profiles-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, yaml);
        return path;
    }

    private static Func<string, string?> Env(params (string, string)[] values) => name => values.FirstOrDefault(v => v.Item1 == name).Item2;

    [Fact]
    public void The_mock_profile_is_always_present_runnable_and_the_default()
    {
        var all = ProfileCatalog.LoadAll(Write("profiles: []"), Env());

        var mock = Assert.Single(all);
        Assert.Equal("mock", mock.Name);
        Assert.True(mock.Runnable);
        Assert.Equal("scripted-mock", mock.ModelId);
    }

    [Fact]
    public void A_profile_without_its_environment_is_skipped_no_credential_and_names_the_variables_not_their_values()
    {
        var all = ProfileCatalog.LoadAll(Write(Yaml("unregistered-provider")), Env(("P_KEY", "super-secret-value")));

        var p = all.Single(x => x.Name == "p-test");
        Assert.False(p.Runnable);
        Assert.StartsWith("skipped:no-credential", p.SkipReason);
        Assert.Contains("P_MODEL", p.SkipReason);
        Assert.DoesNotContain("super-secret-value", p.SkipReason);
    }

    [Fact]
    public void A_configured_profile_with_no_registered_factory_is_skipped_no_provider()
    {
        var all = ProfileCatalog.LoadAll(Write(Yaml("unregistered-provider")), Env(("P_KEY", "k"), ("P_MODEL", "some-model")));

        var p = all.Single(x => x.Name == "p-test");
        Assert.StartsWith("skipped:no-provider", p.SkipReason);
        Assert.Equal("some-model", p.ModelId);
    }

    [Fact]
    public void A_registered_factory_makes_the_profile_runnable_and_receives_the_settings()
    {
        ProviderSettings? seen = null;
        ProviderFactories.Register("provider-under-test", s => { seen = s; return new ExitInterviewAgent.Agent.Mock.ScriptedChatClient(); });

        var p = ProfileCatalog.LoadAll(Write(Yaml("provider-under-test")), Env(("P_KEY", "k"), ("P_MODEL", "m1"), ("P_ENDPOINT", "http://localhost:1"))).Single(x => x.Name == "p-test");
        var client = p.Factory!();

        Assert.True(p.Runnable);
        Assert.IsAssignableFrom<IChatClient>(client);
        Assert.Equal("m1", seen!.Model);
        Assert.Equal("http://localhost:1", seen.Endpoint);
        Assert.Equal("provider-under-test", seen.Provider);
    }

    [Fact]
    public void The_judge_profile_resolves_the_same_way()
    {
        var judge = ProfileCatalog.LoadJudge(Write(Yaml("unregistered-provider")), Env());

        Assert.NotNull(judge);
        Assert.StartsWith("skipped:no-credential", judge.SkipReason);
    }

    [Fact]
    public void The_committed_profiles_name_providers_and_environment_variables_but_commit_no_model_identifier_or_secret()
    {
        var text = File.ReadAllText(RepoLayout.ProfilesPath);
        var all = ProfileCatalog.LoadAll(env: Env());

        Assert.Contains(all, p => p.Name == "anthropic");
        Assert.Contains(all, p => p.Name == "openai-compatible");
        Assert.Contains(all, p => p.Name == "ollama");
        Assert.NotNull(ProfileCatalog.LoadJudge(env: Env()));
        Assert.DoesNotContain("model:", text);
        Assert.DoesNotMatch(@"(?i)claude-|gpt-|llama-?\d|sk-[a-z0-9]{10}", text);
    }

    [Fact]
    public void Registering_a_provider_does_not_make_the_harness_construct_one_by_itself()
    {
        Assert.DoesNotContain(typeof(ModelProfile).Assembly.GetReferencedAssemblies(), a => a.Name!.Contains("Anthropic", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("Ollama", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_same_corpus_run_through_two_profiles_gives_comparable_columns()
    {
        var other = ModelProfile.Mock with { Name = "mock-b" };
        var a = await Reporting.Evaluation.RunProfileAsync(ModelProfile.Mock, Fixtures.All, Fixtures.Labels);
        var b = await Reporting.Evaluation.RunProfileAsync(other, Fixtures.All, Fixtures.Labels);

        Assert.Equal(a.Grades.Count, b.Grades.Count);
        Assert.Equal(Reporting.Evaluation.Pool(a.Grades, "coverage"), Reporting.Evaluation.Pool(b.Grades, "coverage"));
    }
}
