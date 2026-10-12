using System.Net;
using System.Text.Json;
using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Cli.Tiles;

namespace ExitInterviewAgent.Cli.Tests.Tiles;

/// <summary>
/// The platform drafts in the renderer (platform-drafts-spec §3.5, WP4): built from the family tiles by code, shown after the tiles in
/// text, HTML and JSON, in the market order of the page; the JSON carries the notice, as the text and the page do.
/// </summary>
public sealed class PlatformDraftRenderingTests
{
    /// <summary>A set with one accepted structured-review tile, carrying the sections the guard keeps (title, pros, cons, advice).</summary>
    private static TileSet WithStructuredReview(string language) => new(
        TileSet.CurrentVersion,
        language,
        [
            new Tile(
                "t1",
                TileKind.StructuredReview,
                "Notatki z rozmowy.",
                "Notatki z rozmowy.\n\nDobry zespół, jasne zasady, dużo pomocy.\n\nDługi proces wdrożenia i brak informacji zwrotnej.\n\nWarto przejrzeć wdrożenie.",
                ["culture", "onboarding"],
                [
                    new TileSection("title", "Notatki z rozmowy."),
                    new TileSection("pros", "Dobry zespół, jasne zasady, dużo pomocy."),
                    new TileSection("cons", "Długi proces wdrożenia i brak informacji zwrotnej."),
                    new TileSection("advice", "Warto przejrzeć wdrożenie."),
                ]),
        ],
        []);

    private static TileSet Plain(string language) => new(
        TileSet.CurrentVersion,
        language,
        [new Tile("t1", TileKind.Overview, "Krótki opis", "Odejście było spokojne.", ["onboarding"])],
        []);

    private static string Decoded(string html) => WebUtility.HtmlDecode(html);

    [Fact]
    public void Text_shows_the_drafts_after_the_tiles_with_risk_in_words_the_link_and_the_notice_last()
    {
        var text = TileRenderer.ToText(WithStructuredReview("pl"));

        Assert.Contains("GoWork.pl (Wyższe ryzyko) https://www.gowork.pl/\n", text, StringComparison.Ordinal);
        Assert.Contains("Ostrzeżenie: GoWork publikuje opinie", text, StringComparison.Ordinal);
        Assert.Contains("Plusy:\nDobry zespół, jasne zasady, dużo pomocy.\n", text, StringComparison.Ordinal);
        Assert.Contains("Glassdoor (Średnie ryzyko) https://www.glassdoor.com/\n", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("[1] ", StringComparison.Ordinal) < text.IndexOf("Szkice dla serwisów", StringComparison.Ordinal));
        Assert.EndsWith(TileRenderer.NoticePl + "\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_shows_one_card_per_draft_a_copy_button_per_section_and_a_link_that_opens_a_new_tab()
    {
        var html = Decoded(TileRenderer.ToHtml(WithStructuredReview("en")));

        Assert.Contains("<a href=\"https://www.glassdoor.com/\" target=\"_blank\" rel=\"noopener noreferrer\">Open Glassdoor</a>", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Copy: Glassdoor Pros\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Copy: GoWork.pl Pros\"", html, StringComparison.Ordinal);
        Assert.Contains("<p class=\"notice\" role=\"note\" lang=\"en\">", html, StringComparison.Ordinal);
        Assert.Contains("You fill in on the site", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Json_carries_the_notice_and_the_drafts_in_the_wire_shape()
    {
        using var doc = JsonDocument.Parse(TileRenderer.ToJson(WithStructuredReview("en")));
        var root = doc.RootElement;

        Assert.Equal(TileRenderer.NoticeEn, root.GetProperty("notice").GetString());
        var glassdoor = root.GetProperty("drafts").EnumerateArray().First();
        Assert.Equal("glassdoor", glassdoor.GetProperty("platform").GetString());
        Assert.Equal("structured_review", glassdoor.GetProperty("family").GetString());
        Assert.Equal("medium", glassdoor.GetProperty("risk").GetString());
        Assert.Equal("https://www.glassdoor.com/", glassdoor.GetProperty("url").GetString());
        var title = glassdoor.GetProperty("sections").EnumerateArray().First();
        Assert.Equal("title", title.GetProperty("key").GetString());
        Assert.Equal("Headline", title.GetProperty("label").GetString());
        Assert.Equal("Notatki z rozmowy.", title.GetProperty("text").GetString());
        Assert.Equal("Employment status and job title", glassdoor.GetProperty("fieldsOnSite").EnumerateArray().First().GetString());
    }

    [Fact]
    public void In_english_the_platforms_without_an_english_market_come_last_and_the_others_keep_their_order()
    {
        using var doc = JsonDocument.Parse(TileRenderer.ToJson(WithStructuredReview("en")));
        var platforms = doc.RootElement.GetProperty("drafts").EnumerateArray().Select(d => d.GetProperty("platform").GetString()!).ToArray();

        Assert.Equal(new[] { "glassdoor", "indeed", "blind", "gowork" }, platforms);
    }

    [Fact]
    public void A_set_without_family_tiles_has_no_drafts_and_no_drafts_heading()
    {
        var text = TileRenderer.ToText(Plain("pl"));
        using var doc = JsonDocument.Parse(TileRenderer.ToJson(Plain("pl")));

        Assert.DoesNotContain("Szkice dla serwisów", text, StringComparison.Ordinal);
        Assert.Empty(doc.RootElement.GetProperty("drafts").EnumerateArray());
        Assert.Equal(TileRenderer.NoticePl, doc.RootElement.GetProperty("notice").GetString());
    }
}
