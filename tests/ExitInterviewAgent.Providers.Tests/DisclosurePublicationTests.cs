using ExitInterviewAgent.Providers.Tests.Support;

namespace ExitInterviewAgent.Providers.Tests;

/// <summary>Y4: the external notice says that the same provider writes publication drafts from the conversation, in English and in Polish.</summary>
public class DisclosurePublicationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eia-disc-pub-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Theory]
    [InlineData(ProviderKind.Anthropic)]
    [InlineData(ProviderKind.OpenAiCompatible)]
    public void The_external_notice_says_drafts_for_publication_are_written_from_the_conversation_in_english_and_polish(ProviderKind kind)
    {
        var notice = Disclosure.For(Settings.For(kind));

        Assert.Contains("draft texts for publication", notice.Text);
        Assert.Contains("nothing publishes them", notice.Text);
        Assert.Contains("teksty do publikacji wygenerowane z rozmowy", notice.Text);
        Assert.Contains("nic ich nie publikuje", notice.Text);
    }

    [Fact]
    public void A_confirmation_remembered_under_an_older_notice_version_is_asked_again()
    {
        var settings = Settings.For(ProviderKind.Anthropic);
        var path = Path.Combine(_dir, "confirmations.json");
        File.WriteAllText(path, $$"""{ "confirmed": [ { "provider": "{{settings.Info.Id}}", "endpoint": "{{settings.Endpoint}}", "noticeVersion": 1 } ] }""");

        Assert.False(new ConfirmationStore(path).IsConfirmed(settings));
    }
}
