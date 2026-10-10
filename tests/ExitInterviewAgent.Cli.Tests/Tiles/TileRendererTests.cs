using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Cli.Tiles;

namespace ExitInterviewAgent.Cli.Tests.Tiles;

public sealed class TileRendererTests
{
    private const string NoticePlText = "To są propozycje tekstów, nie fakty.";
    private const string NoticeEnText = "These are draft texts, not facts.";

    private static TileSet Sample(string language = "pl") => new(
        TileSet.CurrentVersion,
        language,
        [
            new Tile("t1", TileKind.Overview, "Krótki opis", "Odejście było spokojne.", ["onboarding", "culture"]),
            new Tile("t2", TileKind.ShortNote, "Notka", "Dwa zdania bez nazw.", ["growth"]),
        ],
        [new DroppedTile(TileKind.WhatCouldImprove, TileDropReason.Ungrounded)]);

    private static TileSet Hostile(string language = "en") => new(
        TileSet.CurrentVersion,
        language,
        [new Tile("t1", TileKind.Facts, "<script>alert(1)</script>", "it's \"quoted\" & <b>bold</b> > done", ["culture"])],
        []);

    private static TileSet Empty(string language) => new(TileSet.CurrentVersion, language, [], []);

    private static string Decoded(string html) => WebUtility.HtmlDecode(html);

    private static int Count(string html, string literal) => Regex.Matches(html, Regex.Escape(literal), RegexOptions.IgnoreCase).Count;

    [Fact]
    public void Html_encodes_every_value_that_comes_from_the_set()
    {
        var html = TileRenderer.ToHtml(Hostile());

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("it&#39;s &quot;quoted&quot; &amp; &lt;b&gt;bold&lt;/b&gt; &gt; done", html);
        // Exactly one script element: the page's own copy helper.
        Assert.Equal(1, Count(html, "<script"));
    }

    [Fact]
    public void Html_loads_nothing_from_outside_and_references_no_resource()
    {
        var html = TileRenderer.ToHtml(Sample());

        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
        Assert.DoesNotMatch(new Regex("\\b(src|href)\\s*=", RegexOptions.IgnoreCase), html);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@import", html);
        Assert.DoesNotContain("url(", html);
    }

    [Fact]
    public void Content_security_policy_allows_no_network_and_only_the_hashed_script()
    {
        var html = TileRenderer.ToHtml(Sample());
        var csp = Regex.Match(html, "http-equiv=\"Content-Security-Policy\" content=\"([^\"]*)\"").Groups[1].Value;

        Assert.Contains("default-src 'none'", csp);
        Assert.Contains("style-src 'unsafe-inline'", csp);
        Assert.Matches("script-src 'sha256-[A-Za-z0-9+/=]+'", csp);
        Assert.DoesNotContain("script-src 'unsafe-inline'", csp);
    }

    [Fact]
    public void Csp_script_hash_matches_the_inline_script_in_the_page()
    {
        var html = TileRenderer.ToHtml(Sample());
        var start = html.IndexOf("<script>", StringComparison.Ordinal) + "<script>".Length;
        var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        var script = html[start..end];

        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)));
        var csp = Regex.Match(html, "http-equiv=\"Content-Security-Policy\" content=\"([^\"]*)\"").Groups[1].Value;

        Assert.Contains($"'sha256-{hash}'", csp);
    }

    [Theory]
    [InlineData("pl")]
    [InlineData("en")]
    public void Notice_is_in_the_text_output_at_the_end(string language)
    {
        var text = TileRenderer.ToText(Sample(language));
        var notice = language == "pl" ? NoticePlText : NoticeEnText;

        Assert.Contains(notice, text);
        Assert.EndsWith(language == "pl" ? TileRenderer.NoticePl + "\n" : TileRenderer.NoticeEn + "\n", text);
    }

    [Theory]
    [InlineData("pl")]
    [InlineData("en")]
    public void Notice_is_at_the_top_of_the_html_page(string language)
    {
        var html = TileRenderer.ToHtml(Sample(language));
        var notice = language == "pl" ? NoticePlText : NoticeEnText;

        Assert.Contains(notice, Decoded(html));
        Assert.True(html.IndexOf("class=\"notice\"", StringComparison.Ordinal) < html.IndexOf("<main>", StringComparison.Ordinal));
    }

    [Fact]
    public void Both_notice_variants_are_present_in_both_languages_of_the_source()
    {
        Assert.Contains(NoticePlText, TileRenderer.NoticePl);
        Assert.Contains("This is not legal advice.", TileRenderer.NoticeEn);
        Assert.Contains("To nie jest porada prawna.", TileRenderer.NoticePl);
    }

    [Fact]
    public void Text_numbers_the_blocks_names_the_topics_and_lists_dropped_codes()
    {
        var text = TileRenderer.ToText(Sample("pl"));

        Assert.Contains("[1] Krótki opis\nOdejście było spokojne.\n(na podstawie: onboarding, culture)\n", text);
        Assert.Contains("[2] Notka\nDwa zdania bez nazw.\n(na podstawie: growth)\n", text);
        Assert.Contains("Odrzucone: 1 (kody: ungrounded)", text);
    }

    [Fact]
    public void Text_counts_every_dropped_tile_but_lists_each_code_once()
    {
        var set = Sample("en") with
        {
            Dropped =
            [
                new DroppedTile(TileKind.Overview, TileDropReason.PiiFound),
                new DroppedTile(TileKind.ShortNote, TileDropReason.PiiFound),
                new DroppedTile(TileKind.Facts, TileDropReason.TooLong),
            ],
        };

        Assert.Contains("Dropped: 3 (codes: pii_found, too_long)", TileRenderer.ToText(set));
    }

    [Theory]
    [InlineData("pl", "Brak kafelków.")]
    [InlineData("en", "No tiles.")]
    public void Empty_set_renders_the_notice_and_a_no_tiles_message(string language, string noTiles)
    {
        var text = TileRenderer.ToText(Empty(language));
        var html = TileRenderer.ToHtml(Empty(language));

        Assert.StartsWith(noTiles, text);
        Assert.EndsWith("\n", text);
        Assert.Contains(language == "pl" ? TileRenderer.NoticePl : TileRenderer.NoticeEn, text);
        Assert.Contains(noTiles, Decoded(html));
        Assert.Contains(language == "pl" ? TileRenderer.NoticePl : TileRenderer.NoticeEn, Decoded(html));
        Assert.DoesNotContain("class=\"card\"", html);
    }

    [Fact]
    public void The_same_input_gives_byte_identical_output()
    {
        Assert.Equal(TileRenderer.ToText(Sample()), TileRenderer.ToText(Sample()));
        Assert.Equal(TileRenderer.ToHtml(Sample()), TileRenderer.ToHtml(Sample()));
        Assert.Equal(TileRenderer.ToJson(Sample()), TileRenderer.ToJson(Sample()));
    }

    [Fact]
    public void Html_has_one_card_and_one_labelled_copy_button_per_tile()
    {
        var html = TileRenderer.ToHtml(Sample("en"));

        Assert.Equal(2, Count(html, "class=\"card\""));
        Assert.Equal(2, Count(html, "class=\"copy\""));
        Assert.Contains("aria-label=\"Copy: Krótki opis\"", Decoded(html));
        Assert.Contains("<html lang=\"en\">", html);
    }

    [Fact]
    public void Json_uses_camel_case_names_and_snake_case_kinds()
    {
        var json = TileRenderer.ToJson(Sample());

        Assert.Contains("\"tilesVersion\": \"1\"", json);
        Assert.Contains("\"basedOn\": [", json);
        Assert.Contains("\"reasonCode\": \"ungrounded\"", json);
        Assert.Contains("\"kind\": \"overview\"", json);
        Assert.Contains("\"kind\": \"short_note\"", json);
        Assert.Contains("\"kind\": \"what_could_improve\"", json);
        Assert.DoesNotContain("ShortNote", json);
        Assert.DoesNotContain("TilesVersion", json);
    }

    [Fact]
    public void Json_round_trips_to_the_same_tile_set()
    {
        var json = TileRenderer.ToJson(Sample());

        var back = JsonSerializer.Deserialize<TileSet>(json, TileRenderer.JsonOptions);

        Assert.NotNull(back);
        Assert.Equal(TileSet.CurrentVersion, back.TilesVersion);
        Assert.Equal("pl", back.Language);
        Assert.Equal(TileKind.Overview, back.Tiles[0].Kind);
        Assert.Equal(new[] { "onboarding", "culture" }, back.Tiles[0].BasedOn);
        Assert.Equal(TileDropReason.Ungrounded, back.Dropped[0].ReasonCode);
        Assert.Equal(json, TileRenderer.ToJson(back));
    }
}
