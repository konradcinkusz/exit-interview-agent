using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Cli.Tiles;

namespace ExitInterviewAgent.Cli.Tests.Tiles;

/// <summary>Platform kinds in the renderer (ADR-0075): labels, the wide Reddit card, encoding, the CSP hash, the length notice.</summary>
public sealed class PlatformTileRendererTests
{
    private const string Hostile = "<img src=x onerror=alert(1)> \"quoted\" & <b>bold</b>";

    private static TileSet Platform(string language) => new(
        TileSet.CurrentVersion,
        language,
        [
            new Tile("t1", TileKind.Glassdoor, "Wpis", "Plusy: dobry zespół.", ["culture"]),
            new Tile("t2", TileKind.GoogleReview, "Opinia", "Dwa zdania.", ["onboarding"]),
            new Tile("t3", TileKind.Reddit, "Relacja", "Pierwszoosobowa relacja.", ["management"]),
        ],
        []);

    private static TileSet OneHostile(TileKind kind) => new(TileSet.CurrentVersion, "en", [new Tile("t1", kind, Hostile, Hostile, ["culture"])], []);

    private static string Decoded(string html) => WebUtility.HtmlDecode(html);

    private static int Occurrences(string html, string literal) => Regex.Matches(html, Regex.Escape(literal)).Count;

    [Fact]
    public void Each_platform_kind_has_a_visible_label_in_polish_and_in_english()
    {
        var pl = Decoded(TileRenderer.ToHtml(Platform("pl")));
        var en = Decoded(TileRenderer.ToHtml(Platform("en")));

        Assert.Contains("Glassdoor", pl, StringComparison.Ordinal);
        Assert.Contains("Opinia Google", pl, StringComparison.Ordinal);
        Assert.Contains("Reddit", pl, StringComparison.Ordinal);
        Assert.Contains("Glassdoor", en, StringComparison.Ordinal);
        Assert.Contains("Google review", en, StringComparison.Ordinal);
        Assert.Contains("Reddit", en, StringComparison.Ordinal);
    }

    [Fact]
    public void The_terminal_text_shows_the_label_of_each_platform_kind()
    {
        var text = TileRenderer.ToText(Platform("en"));

        Assert.Contains("Glassdoor", text, StringComparison.Ordinal);
        Assert.Contains("Google review", text, StringComparison.Ordinal);
        Assert.Contains("Reddit", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_reddit_card_is_wide_and_every_card_keeps_its_copy_button()
    {
        var html = TileRenderer.ToHtml(Platform("en"));

        Assert.Contains("<article class=\"card wide\">", html, StringComparison.Ordinal);
        Assert.Equal(2, Occurrences(html, "<article class=\"card\">"));
        Assert.Equal(3, Occurrences(html, "class=\"copy\""));
        Assert.Contains(".card.wide", html, StringComparison.Ordinal);
        Assert.Contains("grid-column: 1 / -1", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TileKind.Glassdoor)]
    [InlineData(TileKind.GoogleReview)]
    [InlineData(TileKind.Reddit)]
    public void Every_platform_kind_encodes_its_title_and_text(TileKind kind)
    {
        var html = TileRenderer.ToHtml(OneHostile(kind));

        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(html, "<script"));
    }

    [Fact]
    public void The_content_security_policy_hash_still_matches_the_only_inline_script()
    {
        var html = TileRenderer.ToHtml(Platform("pl"));

        var match = Regex.Match(html, "<script>(?<body>[\\s\\S]*?)</script>");
        Assert.True(match.Success);
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(match.Groups["body"].Value)));
        Assert.Contains($"'sha256-{hash}'", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_platform_page_loads_nothing_from_the_network()
    {
        var html = TileRenderer.ToHtml(Platform("en"));

        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex("\\b(src|href)\\s*=", RegexOptions.IgnoreCase), html);
        Assert.DoesNotContain("url(", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_length_notice_says_limits_are_approximate_and_offers_the_company_token_in_polish()
    {
        const string sentence = "Limity długości są orientacyjne; sprawdź aktualne zasady platformy. Nazwę firmy wstaw sam albo zostaw [FIRMA].";

        Assert.Contains(sentence, TileRenderer.ToText(Platform("pl")), StringComparison.Ordinal);
        Assert.Contains(sentence, Decoded(TileRenderer.ToHtml(Platform("pl"))), StringComparison.Ordinal);
    }

    [Fact]
    public void The_length_notice_says_limits_are_approximate_and_offers_the_company_token_in_english()
    {
        const string sentence = "Length limits are approximate; check the platform's current rules. Insert the company name yourself or leave [COMPANY].";

        Assert.Contains(sentence, TileRenderer.ToText(Platform("en")), StringComparison.Ordinal);
        Assert.Contains(sentence, Decoded(TileRenderer.ToHtml(Platform("en"))), StringComparison.Ordinal);
    }

    [Fact]
    public void The_json_carries_the_platform_kinds_as_snake_case_names()
    {
        var json = TileRenderer.ToJson(Platform("en"));

        Assert.Contains("\"kind\": \"glassdoor\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"google_review\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"reddit\"", json, StringComparison.Ordinal);
    }
}
