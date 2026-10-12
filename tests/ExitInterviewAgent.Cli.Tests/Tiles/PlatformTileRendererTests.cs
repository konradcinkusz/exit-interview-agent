using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Cli.Tiles;

namespace ExitInterviewAgent.Cli.Tests.Tiles;

/// <summary>Family kinds in the renderer (ADR-0075, ADR-0083): labels, the wide narrative card, encoding, the CSP hash, the length notice.</summary>
public sealed class PlatformTileRendererTests
{
    private const string Hostile = "<img src=x onerror=alert(1)> \"quoted\" & <b>bold</b>";

    private static TileSet Platform(string language) => new(
        TileSet.CurrentVersion,
        language,
        [
            new Tile("t1", TileKind.StructuredReview, "Wpis", "Plusy: dobry zespół.", ["culture"]),
            new Tile("t2", TileKind.ProfessionalPost, "Opinia", "Dwa zdania.", ["onboarding"]),
            new Tile("t3", TileKind.Narrative, "Relacja", "Pierwszoosobowa relacja.", ["management"]),
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

        Assert.Contains("Szkic: opinia", pl, StringComparison.Ordinal);
        Assert.Contains("Szkic: wpis", pl, StringComparison.Ordinal);
        Assert.Contains("Szkic: relacja", pl, StringComparison.Ordinal);
        Assert.Contains("Draft: review", en, StringComparison.Ordinal);
        Assert.Contains("Draft: post", en, StringComparison.Ordinal);
        Assert.Contains("Draft: narrative", en, StringComparison.Ordinal);
    }

    [Fact]
    public void The_terminal_text_shows_the_label_of_each_family_kind()
    {
        var text = TileRenderer.ToText(Platform("en"));

        Assert.Contains("Draft: review", text, StringComparison.Ordinal);
        Assert.Contains("Draft: post", text, StringComparison.Ordinal);
        Assert.Contains("Draft: narrative", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_narrative_card_is_wide_and_every_card_keeps_its_copy_button()
    {
        var html = TileRenderer.ToHtml(Platform("en"));

        Assert.Contains("<article class=\"card wide\">", html, StringComparison.Ordinal);
        Assert.Equal(2, Occurrences(html, "<article class=\"card\">"));
        Assert.Equal(3, Occurrences(html, "class=\"copy\""));
        Assert.Contains(".card.wide", html, StringComparison.Ordinal);
        Assert.Contains("grid-column: 1 / -1", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TileKind.StructuredReview)]
    [InlineData(TileKind.ProfessionalPost)]
    [InlineData(TileKind.Narrative)]
    [InlineData(TileKind.ShortPost)]
    public void Every_family_kind_encodes_its_title_and_text(TileKind kind)
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
    public void The_json_carries_the_family_kinds_as_snake_case_names()
    {
        var json = TileRenderer.ToJson(Platform("en"));

        Assert.Contains("\"kind\": \"structured_review\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"professional_post\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"narrative\"", json, StringComparison.Ordinal);
    }
}
