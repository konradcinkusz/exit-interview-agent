using ExitInterviewAgent.Providers.Tests.Support;

namespace ExitInterviewAgent.Providers.Tests;

public class DisclosureTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eia-disclosure-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Theory]
    [InlineData(ProviderKind.Anthropic)]
    [InlineData(ProviderKind.OpenAiCompatible)]
    public void An_external_provider_notice_says_what_is_sent_where_under_whose_terms_and_requires_a_yes(ProviderKind kind)
    {
        var settings = Settings.For(kind);

        var notice = Disclosure.For(settings);

        Assert.True(notice.RequiresConfirmation);
        Assert.Contains(settings.Endpoint, notice.Text);
        Assert.Contains(settings.Info.DisplayName, notice.Text);
        Assert.Contains("every question and every answer", notice.Text);
        Assert.Contains("your own API key", notice.Text);
        Assert.Contains("terms, data retention and training rules", notice.Text);
        Assert.Contains("not verified", notice.Text);
        Assert.Contains("not sent to this project", notice.Text);
        Assert.Contains("--save-transcript", notice.Text);
        Assert.Contains("--provider ollama", notice.Text);
        Assert.Contains(ProviderCatalog.ReadmeAnchor, notice.Text);
        Assert.DoesNotContain(Settings.Key, notice.Text);
    }

    [Fact]
    public void A_local_model_gets_a_shorter_different_notice_with_no_confirmation()
    {
        var external = Disclosure.For(Settings.For(ProviderKind.Anthropic));

        var local = Disclosure.For(Settings.For(ProviderKind.Ollama));

        Assert.False(local.RequiresConfirmation);
        Assert.True(local.Text.Length < external.Text.Length);
        Assert.Contains("on this computer", local.Text);
        Assert.Contains("licence of the model weights", local.Text);
        Assert.DoesNotContain("terms, data retention", local.Text);
    }

    [Fact]
    public void A_local_gateway_warns_that_it_may_forward_and_a_remote_ollama_is_treated_as_external()
    {
        var gateway = Disclosure.For(Settings.For(ProviderKind.OpenAiCompatible, baseUrl: "http://localhost:1234/v1") with { ApiKey = null });
        var remoteOllama = Disclosure.For(Settings.For(ProviderKind.Ollama, baseUrl: "http://gpu-box.lan:11434"));

        Assert.False(gateway.RequiresConfirmation);
        Assert.Contains("may itself forward", gateway.Text);
        Assert.True(remoteOllama.RequiresConfirmation);
        Assert.Contains("gpu-box.lan:11434", remoteOllama.Text);
    }

    [Fact]
    public void The_mock_notice_says_it_is_a_test_seam()
    {
        var mock = Disclosure.For(Settings.For(ProviderKind.Ollama) with { Kind = ProviderKind.Mock });

        Assert.False(mock.RequiresConfirmation);
        Assert.Contains("test seam", mock.Text);
    }

    [Fact]
    public void A_remembered_confirmation_is_per_provider_and_host_stores_no_key_and_can_be_deleted()
    {
        var store = new ConfirmationStore(Path.Combine(_dir, "nested", "confirmations.json"));
        var anthropic = Settings.For(ProviderKind.Anthropic);

        Assert.False(store.IsConfirmed(anthropic));
        store.Remember(anthropic);

        Assert.True(store.IsConfirmed(anthropic));
        Assert.False(store.IsConfirmed(Settings.For(ProviderKind.OpenAiCompatible)));
        Assert.False(store.IsConfirmed(Settings.For(ProviderKind.Anthropic, baseUrl: "https://other-gateway.example")));
        var text = File.ReadAllText(store.Path);
        Assert.DoesNotContain(Settings.Key, text);
        Assert.DoesNotContain("claude-test-1", text);
        Assert.Equal(1, store.Count);
        Assert.True(store.Forget());
        Assert.False(store.IsConfirmed(anthropic));
        Assert.False(store.Forget());
    }

    [Fact]
    public void A_corrupt_or_oversized_preference_file_counts_as_no_confirmation_instead_of_failing()
    {
        var path = Path.Combine(_dir, "c.json");
        var store = new ConfirmationStore(path);
        File.WriteAllText(path, "{ not json");
        Assert.False(store.IsConfirmed(Settings.For(ProviderKind.Anthropic)));
        File.WriteAllText(path, new string(' ', 70_000));
        Assert.False(store.IsConfirmed(Settings.For(ProviderKind.Anthropic)));
        store.Remember(Settings.For(ProviderKind.Anthropic));
        Assert.True(store.IsConfirmed(Settings.For(ProviderKind.Anthropic)));
    }

    [Fact]
    public void The_preference_file_is_readable_by_its_owner_only_on_unix()
    {
        if (OperatingSystem.IsWindows()) return;
        var store = new ConfirmationStore(Path.Combine(_dir, "p.json"));

        store.Remember(Settings.For(ProviderKind.Anthropic));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(store.Path));
    }
}
