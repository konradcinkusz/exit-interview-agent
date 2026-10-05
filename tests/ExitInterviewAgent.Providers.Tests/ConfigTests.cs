using System.Text.Json;
using ExitInterviewAgent.Providers.Tests.Support;

namespace ExitInterviewAgent.Providers.Tests;

public class ConfigTests
{
    private static Func<string, string?> Env(params (string, string)[] pairs)
    {
        var d = pairs.ToDictionary(p => p.Item1, p => p.Item2);
        return k => d.GetValueOrDefault(k);
    }

    private static UserConfig File(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return UserConfig.Parse(doc.RootElement);
    }

    // ---- precedence ----------------------------------------------------------------------------------------------

    [Fact]
    public void A_flag_beats_the_environment_which_beats_the_file_which_beats_the_default()
    {
        var file = File("""{ "provider": "ollama", "model": "from-file", "baseUrl": "http://file.example:1" }""");
        var env = Env((EnvVars.Model, "from-env"), (EnvVars.BaseUrl, "http://env.example:2"));

        var fileOnly = ProviderConfigResolver.Resolve(new ProviderCliOptions(), Env(), file);
        var withEnv = ProviderConfigResolver.Resolve(new ProviderCliOptions(), env, file);
        var withFlag = ProviderConfigResolver.Resolve(new ProviderCliOptions { Model = "from-flag", BaseUrl = "http://flag.example:3" }, env, file);
        var defaults = ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "ollama", Model = "m" }, Env(), UserConfig.Empty);

        Assert.Equal(("from-file", "file.example:1"), (fileOnly.Settings.Model, fileOnly.Settings.Endpoint));
        Assert.Equal(("from-env", "env.example:2"), (withEnv.Settings.Model, withEnv.Settings.Endpoint));
        Assert.Equal(("from-flag", "flag.example:3"), (withFlag.Settings.Model, withFlag.Settings.Endpoint));
        Assert.Equal("localhost:11434", defaults.Settings.Endpoint);
        Assert.Equal(ConfigSource.ConfigFile, fileOnly.Sources["model"]);
        Assert.Equal(ConfigSource.Environment, withEnv.Sources["model"]);
        Assert.Equal(ConfigSource.CommandLine, withFlag.Sources["model"]);
        Assert.Equal(ConfigSource.Default, defaults.Sources["baseUrl"]);
    }

    [Fact]
    public void The_provider_comes_from_a_flag_then_the_environment_then_the_file()
    {
        var file = File("""{ "provider": "ollama", "model": "m" }""");

        Assert.Equal(ProviderKind.Ollama, ProviderConfigResolver.Resolve(new ProviderCliOptions(), Env(), file).Settings.Kind);
        Assert.Equal(ProviderKind.Anthropic, ProviderConfigResolver.Resolve(new ProviderCliOptions(), Env((EnvVars.Provider, "anthropic"), ("ANTHROPIC_API_KEY", Settings.Key)), file).Settings.Kind);
        Assert.Equal(ProviderKind.OpenAiCompatible, ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "openai" }, Env((EnvVars.Provider, "anthropic"), ("OPENAI_API_KEY", Settings.Key)), file).Settings.Kind);
    }

    [Fact]
    public void Missing_provider_or_model_is_an_error_that_names_every_way_to_set_it()
    {
        var noProvider = Assert.Throws<ProviderConfigurationException>(() => ProviderConfigResolver.Resolve(new ProviderCliOptions { Model = "m" }, Env()));
        var noModel = Assert.Throws<ProviderConfigurationException>(() => ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "ollama" }, Env()));

        Assert.Contains("--provider", noProvider.Message);
        Assert.Contains(EnvVars.Provider, noProvider.Message);
        Assert.Contains("--model", noModel.Message);
        Assert.Contains(EnvVars.Model, noModel.Message);
    }

    [Fact]
    public void Provider_specific_base_url_variables_are_read_and_the_generic_one_wins()
    {
        var openAi = ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "openai-compatible", Model = "m" }, Env(("OPENAI_API_KEY", Settings.Key), ("OPENAI_BASE_URL", "https://gw.example/v1")));
        var both = ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "openai-compatible", Model = "m" }, Env(("OPENAI_API_KEY", Settings.Key), ("OPENAI_BASE_URL", "https://gw.example/v1"), (EnvVars.BaseUrl, "https://generic.example/v1")));

        Assert.Equal("gw.example", openAi.Settings.Endpoint);
        Assert.Equal("generic.example", both.Settings.Endpoint);
    }

    [Theory]
    [InlineData("127.0.0.1:11434", "127.0.0.1:11434")]
    [InlineData("0.0.0.0:11434", "127.0.0.1:11434")]
    [InlineData("http://gpu-box:8080", "gpu-box:8080")]
    public void OLLAMA_HOST_may_be_a_bare_host_and_a_bind_all_address_is_not_a_target(string host, string expected)
    {
        var r = ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "ollama", Model = "m" }, Env(("OLLAMA_HOST", host)));

        Assert.Equal(expected, r.Settings.Endpoint);
    }

    [Fact]
    public void Numbers_and_prices_resolve_from_every_source_and_the_cost_ceiling_needs_prices()
    {
        var file = File("""{ "provider": "ollama", "model": "m", "timeoutSeconds": 30, "maxRetries": 1, "maxTokens": 5000, "numCtx": 4096, "prices": { "inputPerMillionTokens": 1.5, "outputPerMillionTokens": 3, "currency": "EUR" } }""");

        var r = ProviderConfigResolver.Resolve(new ProviderCliOptions { MaxRetries = "5" }, Env((EnvVars.MaxCost, "2.5")), file).Settings;

        Assert.Equal(TimeSpan.FromSeconds(30), r.Resilience.RequestTimeout);
        Assert.Equal(5, r.Resilience.MaxRetries);
        Assert.Equal(5000, r.MaxTokens);
        Assert.Equal(4096, r.NumCtx);
        Assert.Equal(new PriceConfig(1.5m, 3m, "EUR"), r.Prices);
        Assert.Equal(2.5m, r.MaxCost);
        Assert.Throws<ProviderConfigurationException>(() => ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "ollama", Model = "m", MaxCost = "1" }, Env()));
        Assert.Throws<ProviderConfigurationException>(() => ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "ollama", Model = "m", PriceInput = "1" }, Env()));
        Assert.Throws<ProviderConfigurationException>(() => ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "ollama", Model = "m", TimeoutSeconds = "abc" }, Env()));
    }

    [Fact]
    public void No_price_is_hardcoded_cost_is_absent_unless_the_user_supplies_prices()
    {
        var r = ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "ollama", Model = "m" }, Env());

        Assert.Null(r.Settings.Prices);
        Assert.Null(new InterviewBudget(1000, 10).Snapshot().Cost);
    }

    // ---- keys ----------------------------------------------------------------------------------------------------

    [Fact]
    public void The_key_is_read_from_the_providers_own_variable_or_the_one_that_is_named()
    {
        var own = ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "anthropic", Model = "m" }, Env(("ANTHROPIC_API_KEY", Settings.Key)));
        var named = ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "anthropic", Model = "m", ApiKeyEnv = "MY_WORK_KEY" }, Env(("MY_WORK_KEY", "sk-other-key-999"), ("ANTHROPIC_API_KEY", Settings.Key)));
        var viaFile = ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "anthropic", Model = "m" }, Env(("FILE_KEY", "sk-file-key-999")), File("""{ "apiKeyEnv": "FILE_KEY" }"""));

        Assert.Equal("ANTHROPIC_API_KEY", own.KeyVariable);
        Assert.Equal(Settings.Key, own.Settings.ApiKey!.Reveal());
        Assert.Equal("MY_WORK_KEY", named.KeyVariable);
        Assert.Equal("sk-other-key-999", named.Settings.ApiKey!.Reveal());
        Assert.Equal("sk-file-key-999", viaFile.Settings.ApiKey!.Reveal());
    }

    [Fact]
    public void A_missing_key_names_the_variable_to_set_and_never_a_value()
    {
        var ex = Assert.Throws<ProviderConfigurationException>(() => ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "anthropic", Model = "m" }, Env()));

        Assert.Contains("ANTHROPIC_API_KEY", ex.Message);
        Assert.Throws<ProviderConfigurationException>(() => ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "openai-compatible", Model = "m" }, Env()));
        // A gateway you point at yourself may need no key.
        Assert.Null(ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "openai-compatible", Model = "m", BaseUrl = "http://localhost:1234/v1" }, Env()).Settings.ApiKey);
    }

    [Fact]
    public void A_pasted_key_in_place_of_a_variable_name_is_rejected_without_being_echoed()
    {
        var pasted = "sk-ant-api03-PASTED-SECRET-VALUE";

        var ex = Assert.Throws<ProviderConfigurationException>(() => ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "anthropic", Model = "m", ApiKeyEnv = pasted }, Env()));

        Assert.DoesNotContain("PASTED", ex.Message);
        Assert.Contains("NAME", ex.Message);
    }

    [Fact]
    public void The_secret_cannot_be_printed_by_accident()
    {
        var settings = Settings.For(ProviderKind.Anthropic);

        Assert.DoesNotContain(Settings.Key, settings.ToString());
        Assert.DoesNotContain(Settings.Key, settings.ApiKey!.ToString());
        Assert.DoesNotContain(Settings.Key, JsonSerializer.Serialize(settings.ApiKey));
        Assert.Throws<ProviderConfigurationException>(() => new SecretString("with space"));
        Assert.Throws<ProviderConfigurationException>(() => new SecretString("line\nbreak"));
    }

    [Theory]
    [InlineData("apiKey")]
    [InlineData("api_key")]
    [InlineData("token")]
    [InlineData("password")]
    public void A_config_file_with_a_key_in_it_is_refused(string property)
    {
        var ex = Assert.Throws<ProviderConfigurationException>(() => File($$"""{ "provider": "anthropic", "{{property}}": "sk-in-a-file-12345" }"""));

        Assert.Contains("apiKeyEnv", ex.Message);
        Assert.DoesNotContain("sk-in-a-file", ex.Message);
    }

    [Fact]
    public void A_config_file_is_strict_about_unknown_properties_and_types_and_does_not_echo_odd_names()
    {
        Assert.Contains("provder", Assert.Throws<ProviderConfigurationException>(() => File("""{ "provder": "x" }""")).Message);
        Assert.Throws<ProviderConfigurationException>(() => File("""{ "maxRetries": "three" }"""));
        Assert.Throws<ProviderConfigurationException>(() => File("[]"));
        var odd = Assert.Throws<ProviderConfigurationException>(() => File("""{ "sk-ant-weird/secret": 1 }"""));
        Assert.DoesNotContain("sk-ant", odd.Message);
    }

    [Fact]
    public void The_config_file_loads_from_disk_is_optional_and_errors_do_not_quote_it()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            Assert.Same(UserConfig.Empty, UserConfig.Load(Path.Combine(dir.FullName, "missing.json")));
            var path = Path.Combine(dir.FullName, "config.json");
            System.IO.File.WriteAllText(path, """{ "provider": "ollama", "model": "llama" } """);
            Assert.Equal("llama", UserConfig.Load(path).Model);
            System.IO.File.WriteAllText(path, "{ not json SECRET-IN-FILE");
            var ex = Assert.Throws<ProviderConfigurationException>(() => UserConfig.Load(path));
            Assert.DoesNotContain("SECRET-IN-FILE", ex.Message);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Config_locations_follow_the_override_then_XDG_then_home()
    {
        Assert.Equal("/x/y/config.json", ConfigPaths.ConfigFile(Env((EnvVars.Config, "/x/y/config.json"))));
        Assert.Equal(Path.Combine("/cfg", "exit-interview", "config.json"), ConfigPaths.ConfigFile(Env(("XDG_CONFIG_HOME", "/cfg"))));
        Assert.Equal(Path.Combine("/home/u", ".config", "exit-interview", "confirmations.json"), ConfigPaths.ConfirmationsFile(Env(("HOME", "/home/u"))));
        Assert.Equal(Path.Combine("/d", "config.json"), ConfigPaths.ConfigFile(Env((EnvVars.ConfigDir, "/d"))));
    }

    // ---- base URLs -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("https://user:pass@gateway.example/v1")]
    [InlineData("https://TOKEN-IN-USERINFO@gateway.example/v1")]
    public void Embedded_credentials_in_a_base_url_are_refused_and_never_repeated(string url)
    {
        var ex = Assert.Throws<ProviderConfigurationException>(() => BaseUrlPolicy.Validate(url, keyed: true));

        Assert.DoesNotContain("pass", ex.Message);
        Assert.DoesNotContain("TOKEN-IN-USERINFO", ex.Message);
        Assert.DoesNotContain("gateway.example", ex.Message);
        var viaResolver = Assert.Throws<ProviderConfigurationException>(() => ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "ollama", Model = "m", BaseUrl = url }, Env()));
        Assert.DoesNotContain("TOKEN-IN-USERINFO", viaResolver.Message);
    }

    [Theory]
    [InlineData("ftp://host/")]
    [InlineData("not a url")]
    [InlineData("https://host/v1?api_key=SECRET")]
    [InlineData("https://host/v1#frag")]
    public void Odd_base_urls_are_refused(string url) =>
        Assert.Throws<ProviderConfigurationException>(() => BaseUrlPolicy.Validate(url, keyed: false));

    [Fact]
    public void A_key_is_never_sent_over_plain_http_to_another_machine_but_loopback_is_fine()
    {
        Assert.Throws<ProviderConfigurationException>(() => BaseUrlPolicy.Validate("http://gpu-box.lan/v1", keyed: true));
        BaseUrlPolicy.Validate("http://gpu-box.lan:11434", keyed: false);
        BaseUrlPolicy.Validate("http://localhost:1234/v1", keyed: true);
        BaseUrlPolicy.Validate("http://127.0.0.1:1234/v1", keyed: true);
        BaseUrlPolicy.Validate("http://[::1]:1234/v1", keyed: true);
    }

    [Fact]
    public void Local_versus_remote_is_derived_from_the_address_not_from_the_provider_name()
    {
        Assert.True(Settings.For(ProviderKind.Ollama).IsLocal);
        Assert.False(Settings.For(ProviderKind.Ollama, baseUrl: "http://gpu-box.lan:11434").IsLocal);
        Assert.False(Settings.For(ProviderKind.Anthropic).IsLocal);
        Assert.True(Settings.For(ProviderKind.OpenAiCompatible, baseUrl: "http://localhost:1234/v1").IsLocal);
    }

    // ---- unsupported backends and credentials --------------------------------------------------------------------

    [Theory]
    [InlineData("copilot")]
    [InlineData("GitHub-Copilot")]
    [InlineData("github_copilot")]
    public void GitHub_Copilot_is_refused_with_the_could_not_be_verified_wording_never_prohibited(string id)
    {
        var ex = Assert.Throws<UnsupportedProviderException>(() => ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = id, Model = "m" }, Env()));

        Assert.Contains("could not be verified", ex.Message);
        Assert.DoesNotContain("prohibited by", ex.Message);
        Assert.Contains("README.md#run-it-with-your-own-model", ex.Message);
    }

    [Fact]
    public void A_copilot_service_host_cannot_be_reached_through_the_openai_compatible_provider()
    {
        Assert.Throws<UnsupportedProviderException>(() => BaseUrlPolicy.Validate("https://api.githubcopilot.com", keyed: true));
        Assert.Throws<UnsupportedProviderException>(() => ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "openai-compatible", Model = "m", BaseUrl = "https://api.githubcopilot.com" }, Env(("OPENAI_API_KEY", Settings.Key))));
    }

    [Theory]
    [InlineData("claude-subscription")]
    [InlineData("claude-max")]
    [InlineData("claude-ai")]
    [InlineData("subscription")]
    public void Claude_subscription_backends_are_refused_with_a_pointer_to_the_readme(string id)
    {
        var ex = Assert.Throws<UnsupportedProviderException>(() => ProviderCatalog.Get(id));

        Assert.Contains("Free, Pro or Max", ex.Message);
        Assert.Contains("README.md#run-it-with-your-own-model", ex.Message);
        Assert.Contains("--provider anthropic", ex.Message);
    }

    [Theory]
    [InlineData("CLAUDE_CODE_OAUTH_TOKEN")]
    [InlineData("claude_code_oauth_refresh_token")]
    public void The_documented_subscription_token_variables_are_refused_as_a_key_source_by_flag_environment_and_file(string variable)
    {
        var value = "sk-subscription-token-VALUE-123";
        var vars = new[] { (variable, value) };

        var byFlag = Assert.Throws<UnsupportedCredentialException>(() => ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "anthropic", Model = "m", ApiKeyEnv = variable }, Env(vars)));
        var byEnv = Assert.Throws<UnsupportedCredentialException>(() => ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "anthropic", Model = "m" }, Env(vars.Append((EnvVars.ApiKeyEnv, variable)).ToArray())));
        var byFile = Assert.Throws<UnsupportedCredentialException>(() => ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "anthropic", Model = "m" }, Env(vars), File($$"""{ "apiKeyEnv": "{{variable}}" }""")));

        foreach (var ex in new[] { byFlag, byEnv, byFile })
        {
            Assert.Contains("subscription", ex.Message);
            Assert.Contains("README.md#run-it-with-your-own-model", ex.Message);
            Assert.DoesNotContain(value, ex.Message);
        }
    }

    [Fact]
    public void A_subscription_token_that_is_merely_present_is_not_read_and_the_missing_key_message_says_so()
    {
        var env = Env(("CLAUDE_CODE_OAUTH_TOKEN", "sk-subscription-token-VALUE-123"));

        var ex = Assert.Throws<ProviderConfigurationException>(() => ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "anthropic", Model = "m" }, env));
        var status = ProviderConfigResolver.Inspect(env).Single(s => s.Info.Kind == ProviderKind.Anthropic);

        Assert.Contains("not read and is not supported", ex.Message);
        Assert.DoesNotContain("VALUE-123", ex.Message);
        Assert.False(status.KeyPresent);
        Assert.Contains("ignored", status.Note);
    }

    [Fact]
    public void There_is_no_value_based_heuristic_for_recognising_a_subscription_token()
    {
        // The documentation does not describe a token format, so none is guessed: any value in ANTHROPIC_API_KEY is accepted here and left to the API to reject.
        var r = ProviderConfigResolver.Resolve(new ProviderCliOptions { Provider = "anthropic", Model = "m" }, Env(("ANTHROPIC_API_KEY", "sk-ant-oat01-looks-like-an-oauth-token")));

        Assert.NotNull(r.Settings.ApiKey);
    }

    [Fact]
    public void An_unknown_provider_is_refused_without_echoing_what_was_typed()
    {
        var ex = Assert.Throws<ProviderConfigurationException>(() => ProviderCatalog.Get("sk-ant-api03-MISPLACED"));

        Assert.DoesNotContain("MISPLACED", ex.Message);
        Assert.Contains("ollama", ex.Message);
    }

    [Fact]
    public void Inspect_reports_local_facts_for_every_provider_without_touching_the_network()
    {
        var statuses = ProviderConfigResolver.Inspect(Env(("ANTHROPIC_API_KEY", Settings.Key)));

        Assert.Equal(ProviderCatalog.All.Count, statuses.Count);
        Assert.True(statuses.Single(s => s.Info.Kind == ProviderKind.Anthropic).KeyPresent);
        Assert.False(statuses.Single(s => s.Info.Kind == ProviderKind.OpenAiCompatible).KeyPresent);
        Assert.DoesNotContain(Settings.Key, string.Join("|", statuses.Select(s => s.Note + s.Endpoint)));
    }
}
