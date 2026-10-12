using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExitInterviewAgent.Agent.Tiles;
using ExitInterviewAgent.Agent.Tiles.Platforms;
using ExitInterviewAgent.Contracts;

namespace ExitInterviewAgent.Cli.Tiles;

/// <summary>
/// Shows a <see cref="TileSet"/> three ways: numbered plain text for the terminal, one self-contained HTML page, and camelCase
/// JSON (<c>tiles.json</c>). The platform drafts (spec §3.5) are built from the set by <see cref="PlatformDraftBuilder"/> and shown
/// after the tiles in every format, in the order the page shows them. Every value that comes from the set is HTML-encoded. The page
/// loads nothing from the network (no script, font, image or stylesheet link) and runs one inline script whose SHA-256 hash is in its
/// Content-Security-Policy. The fixed notice and the words of the page (headings, risk words, link text) are the only wording here
/// that is not from the set; they are defined in <c>Words</c>, and the notice once, in <see cref="NoticePl"/> and <see cref="NoticeEn"/>.
/// </summary>
internal static class TileRenderer
{
    internal const string NoticePl = "To są propozycje tekstów, nie fakty. Program niczego nie weryfikuje i niczego nie publikuje. Za treść, którą opublikujesz, odpowiadasz Ty; publikacja opinii o pracodawcy może mieć skutki prawne. Przeczytaj i zmień każdy tekst, zanim go użyjesz. To nie jest porada prawna. Limity długości są orientacyjne; sprawdź aktualne zasady platformy. Nazwę firmy wstaw sam albo zostaw [FIRMA].";

    internal const string NoticeEn = "These are draft texts, not facts. The program verifies nothing and publishes nothing. You are responsible for any text you publish; publishing an opinion about an employer can have legal consequences. Read and change every text before you use it. This is not legal advice. Length limits are approximate; check the platform's current rules. Insert the company name yourself or leave [COMPANY].";

    /// <summary>The page's only script: copies one tile's text, or selects it when the clipboard is not available.</summary>
    internal const string CopyScript = """
        document.documentElement.classList.add('js');
        document.querySelectorAll('button.copy').forEach(function (button) {
          button.addEventListener('click', function () {
            var text = document.getElementById(button.getAttribute('data-target'));
            var range = document.createRange();
            range.selectNodeContents(text);
            window.getSelection().removeAllRanges();
            window.getSelection().addRange(range);
            if (navigator.clipboard && window.isSecureContext) {
              navigator.clipboard.writeText(text.textContent).then(function () {
                button.textContent = button.getAttribute('data-done');
              }, function () {});
            }
          });
        });
        """;

    private static readonly string ScriptHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(CopyScript)));

    private static readonly string Csp = $"default-src 'none'; style-src 'unsafe-inline'; script-src 'sha256-{ScriptHash}'; base-uri 'none'; form-action 'none'";

    /// <summary>Serializer options for <c>tiles.json</c>: camelCase names, enum values as snake_case strings.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private const string Css = """
        :root { color-scheme: light dark; --bg: #f7f7f5; --fg: #1b1b1b; --muted: #4b4b4b; --card: #ffffff; --line: #b8b8b2;
          --note-bg: #fff4cc; --note-fg: #3b2a00; --btn-bg: #1a4fa0; --btn-fg: #ffffff; }
        @media (prefers-color-scheme: dark) {
          :root { --bg: #141414; --fg: #f0f0f0; --muted: #c4c4c4; --card: #1e1e1e; --line: #4a4a4a;
            --note-bg: #2e2500; --note-fg: #ffe9a8; --btn-bg: #9ec5ff; --btn-fg: #0a1a33; }
        }
        body { margin: 0; padding: 16px; background: var(--bg); color: var(--fg); font: 16px/1.5 system-ui, sans-serif; }
        h1 { font-size: 1.4rem; margin: 16px 0; }
        .notice { background: var(--note-bg); color: var(--note-fg); border-left: 4px solid #b7791f; padding: 12px 14px; border-radius: 6px; }
        .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(100%, 260px), 1fr)); gap: 12px; }
        .card { background: var(--card); border: 1px solid var(--line); border-radius: 8px; padding: 14px; display: flex; flex-direction: column; gap: 8px; }
        .card.wide { grid-column: 1 / -1; }
        .card h2 { font-size: 1.05rem; margin: 0; }
        .kind { color: var(--muted); font-size: .85rem; font-weight: 600; margin: 0; }
        .meta { color: var(--muted); font-size: .85rem; margin: 0; }
        .text { white-space: pre-line; overflow-wrap: anywhere; margin: 0; }
        .copy { display: none; align-self: flex-start; min-height: 40px; padding: 8px 14px; border: 0; border-radius: 6px; font: inherit; background: var(--btn-bg); color: var(--btn-fg); cursor: pointer; }
        .js .copy { display: inline-block; }
        .fields { margin: 0; padding-left: 1.2em; }
        .dropped { margin-top: 16px; }
        :focus-visible { outline: 3px solid #d97706; outline-offset: 2px; }
        """;

    /// <summary>The tiles as they are written to <c>tiles.json</c>: the notice, the set, and the drafts in the wire's shape (spec §3.4).</summary>
    internal sealed record TileDocument(
        string Notice,
        string TilesVersion,
        string Language,
        IReadOnlyList<Tile> Tiles,
        IReadOnlyList<DroppedTile> Dropped,
        IReadOnlyList<InterviewPlatformDraft> Drafts);

    /// <summary>Terminal and page wording by language. Anything not Polish is shown in English.</summary>
    private sealed record Words(
        string Title,
        string NoTiles,
        string BasedOn,
        string Dropped,
        string Codes,
        string Copy,
        string Copied,
        string Version,
        string Notice,
        string[] KindLabels,
        string Drafts,
        string RiskHigh,
        string RiskMedium,
        string Open,
        string NewTab,
        string Warning,
        string Fields)
    {
        public static readonly Words Pl = new(
            "Propozycje tekstów", "Brak kafelków.", "na podstawie", "Odrzucone", "kody", "Kopiuj", "Skopiowano", "wersja", NoticePl,
            ["Fakty", "Przegląd", "Co się sprawdziło", "Co można poprawić", "Dla następnej osoby", "Szkic: opinia", "Szkic: relacja", "Szkic: wpis", "Szkic: krótki wpis"],
            "Szkice dla serwisów", "Wyższe ryzyko", "Średnie ryzyko", "Otwórz", "otwiera nową kartę", "Ostrzeżenie", "Uzupełnisz w serwisie");

        public static readonly Words En = new(
            "Draft texts", "No tiles.", "based on", "Dropped", "codes", "Copy", "Copied", "version", NoticeEn,
            ["Facts", "Overview", "What worked", "What could improve", "For the next person", "Draft: review", "Draft: narrative", "Draft: post", "Draft: short post"],
            "Drafts for a platform", "Higher risk", "Medium risk", "Open", "opens a new tab", "Warning", "You fill in on the site");

        public static Words For(string language) => IsPolish(language) ? Pl : En;
    }

    /// <summary>Plain text for the terminal: numbered blocks, the drafts, the dropped codes, and the notice last.</summary>
    public static string ToText(TileSet set)
    {
        var w = Words.For(set.Language);
        var text = new StringBuilder();
        if (set.Tiles.Count == 0) text.Append(w.NoTiles).Append("\n\n");
        for (var i = 0; i < set.Tiles.Count; i++)
        {
            var tile = set.Tiles[i];
            text.Append(w.KindLabels[(int)tile.Kind]).Append('\n');
            text.Append('[').Append(i + 1).Append("] ").Append(tile.Title).Append('\n').Append(tile.Text).Append('\n');
            if (tile.BasedOn.Count > 0) text.Append('(').Append(w.BasedOn).Append(": ").Append(string.Join(", ", tile.BasedOn)).Append(")\n");
            text.Append('\n');
        }
        AppendDraftsText(text, w, OrderedDrafts(set));
        if (set.Dropped.Count > 0) text.Append(DroppedLine(w, set.Dropped)).Append("\n\n");
        text.Append(w.Notice).Append('\n');
        return text.ToString();
    }

    /// <summary>One self-contained page: notice at the top, a card per tile and per draft with copy buttons, no network resources.</summary>
    public static string ToHtml(TileSet set)
    {
        var w = Words.For(set.Language);
        var language = IsPolish(set.Language) ? "pl" : "en";
        var html = new StringBuilder();
        html.Append("<!doctype html>\n<html lang=\"").Append(string.IsNullOrWhiteSpace(set.Language) ? "en" : E(set.Language)).Append("\">\n<head>\n")
            .Append("<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
            .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"").Append(Csp).Append("\">\n")
            .Append("<title>").Append(E(w.Title)).Append("</title>\n")
            .Append("<style>\n").Append(Css).Append("\n</style>\n")
            .Append("</head>\n<body>\n<header>\n")
            .Append("<p class=\"notice\" role=\"note\">").Append(E(w.Notice)).Append("</p>\n")
            .Append("<h1>").Append(E(w.Title)).Append("</h1>\n</header>\n<main>\n");
        if (set.Tiles.Count == 0) html.Append("<p>").Append(E(w.NoTiles)).Append("</p>\n");
        html.Append("<div class=\"grid\">\n");
        for (var i = 0; i < set.Tiles.Count; i++)
        {
            var tile = set.Tiles[i];
            var id = "tile-" + (i + 1);
            var wide = tile.Kind == TileKind.Narrative ? " wide" : string.Empty;
            html.Append("<article class=\"card").Append(wide).Append("\">\n<p class=\"kind\">").Append(E(w.KindLabels[(int)tile.Kind])).Append("</p>\n")
                .Append("<h2>").Append(E(tile.Title)).Append("</h2>\n");
            if (tile.BasedOn.Count > 0) html.Append("<p class=\"meta\">(").Append(E(w.BasedOn)).Append(": ").Append(E(string.Join(", ", tile.BasedOn))).Append(")</p>\n");
            html.Append("<p class=\"text\" id=\"").Append(id).Append("\">").Append(E(tile.Text)).Append("</p>\n")
                .Append("<button type=\"button\" class=\"copy\" data-target=\"").Append(id)
                .Append("\" data-done=\"").Append(E(w.Copied))
                .Append("\" aria-label=\"").Append(E(w.Copy + ": " + tile.Title)).Append("\">").Append(E(w.Copy)).Append("</button>\n")
                .Append("</article>\n");
        }
        html.Append("</div>\n");
        AppendDraftsHtml(html, w, language, OrderedDrafts(set));
        if (set.Dropped.Count > 0) html.Append("<p class=\"meta dropped\">").Append(E(DroppedLine(w, set.Dropped))).Append("</p>\n");
        html.Append("</main>\n<footer><p class=\"meta\">").Append(E(w.Version)).Append(' ').Append(E(set.TilesVersion)).Append("</p></footer>\n")
            .Append("<script>").Append(CopyScript).Append("</script>\n")
            .Append("</body>\n</html>\n");
        return html.ToString();
    }

    /// <summary>The set as camelCase JSON, the content of <c>tiles.json</c>: the notice, the tiles, the dropped codes and the drafts.</summary>
    public static string ToJson(TileSet set)
    {
        var w = Words.For(set.Language);
        var drafts = OrderedDrafts(set).Select(ToWire).ToList();
        return JsonSerializer.Serialize(new TileDocument(w.Notice, set.TilesVersion, set.Language, set.Tiles, set.Dropped, drafts), JsonOptions);
    }

    /// <summary>
    /// The drafts in the order the page shows them (spec §3.5): the platforms whose markets include the interview's language first, each
    /// group in profile-file order. The builder never edits a text; this only chooses the order.
    /// </summary>
    private static IReadOnlyList<PlatformDraft> OrderedDrafts(TileSet set)
    {
        var language = IsPolish(set.Language) ? "pl" : "en";
        var profiles = PlatformProfiles.Default;
        return PlatformDraftBuilder.Build(set, profiles, language)
            .OrderBy(d => profiles.Get(d.Id).Markets.Contains(language, StringComparer.Ordinal) ? 0 : 1)
            .ToList();
    }

    /// <summary>A platform draft as the wire carries it (spec §3.4): the same words as the service sends, the texts untouched.</summary>
    private static InterviewPlatformDraft ToWire(PlatformDraft draft) => new(
        draft.Id,
        draft.Name,
        JsonNamingPolicy.SnakeCaseLower.ConvertName(draft.Family.ToString()),
        JsonNamingPolicy.SnakeCaseLower.ConvertName(draft.Risk.ToString()),
        draft.Url,
        draft.Warning,
        draft.Sections.Select(s => new InterviewDraftSection(s.Key, s.Label, s.Text)).ToList(),
        draft.CopyText,
        draft.FieldsOnSite);

    private static void AppendDraftsText(StringBuilder text, Words w, IReadOnlyList<PlatformDraft> drafts)
    {
        if (drafts.Count == 0) return;
        text.Append(w.Drafts).Append("\n\n");
        foreach (var draft in drafts)
        {
            text.Append(draft.Name).Append(" (").Append(RiskWord(w, draft.Risk)).Append(") ").Append(draft.Url).Append('\n');
            text.Append(w.Warning).Append(": ").Append(draft.Warning).Append('\n');
            foreach (var section in draft.Sections)
                text.Append(section.Label).Append(":\n").Append(section.Text).Append('\n');
            if (draft.FieldsOnSite.Count > 0) text.Append(w.Fields).Append(": ").Append(string.Join("; ", draft.FieldsOnSite)).Append('\n');
            text.Append('\n');
        }
    }

    /// <summary>One card per draft: the name and risk in words, the warning, a copy button per section, the fields to fill in, and the link.</summary>
    private static void AppendDraftsHtml(StringBuilder html, Words w, string language, IReadOnlyList<PlatformDraft> drafts)
    {
        if (drafts.Count == 0) return;
        html.Append("<section aria-labelledby=\"drafts-title\">\n<h2 id=\"drafts-title\">").Append(E(w.Drafts)).Append("</h2>\n<div class=\"grid\">\n");
        for (var i = 0; i < drafts.Count; i++)
        {
            var draft = drafts[i];
            var card = "draft-" + (i + 1);
            html.Append("<article class=\"card\">\n<p class=\"kind\">").Append(E(RiskWord(w, draft.Risk))).Append("</p>\n")
                .Append("<h2>").Append(E(draft.Name)).Append("</h2>\n")
                .Append("<p class=\"notice\" role=\"note\" lang=\"").Append(language).Append("\">").Append(E(draft.Warning)).Append("</p>\n");
            for (var k = 0; k < draft.Sections.Count; k++)
            {
                var section = draft.Sections[k];
                var id = card + "-" + (k + 1);
                html.Append("<p class=\"kind\">").Append(E(section.Label)).Append("</p>\n")
                    .Append("<p class=\"text\" id=\"").Append(id).Append("\">").Append(E(section.Text)).Append("</p>\n")
                    .Append("<button type=\"button\" class=\"copy\" data-target=\"").Append(id)
                    .Append("\" data-done=\"").Append(E(w.Copied))
                    .Append("\" aria-label=\"").Append(E(w.Copy + ": " + draft.Name + " " + section.Label)).Append("\">").Append(E(w.Copy)).Append("</button>\n");
            }
            if (draft.FieldsOnSite.Count > 0)
            {
                html.Append("<p class=\"kind\">").Append(E(w.Fields)).Append("</p>\n<ul class=\"fields\">");
                foreach (var field in draft.FieldsOnSite) html.Append("<li>").Append(E(field)).Append("</li>");
                html.Append("</ul>\n");
            }
            // The link is the profile's URL with no parameters; it opens in a new tab and nothing is fetched by this page.
            html.Append("<p class=\"meta\"><a href=\"").Append(E(draft.Url)).Append("\" target=\"_blank\" rel=\"noopener noreferrer\">")
                .Append(E(w.Open + " " + draft.Name)).Append("</a> (").Append(E(w.NewTab)).Append(")</p>\n")
                .Append("</article>\n");
        }
        html.Append("</div>\n</section>\n");
    }

    private static string RiskWord(Words w, PlatformRisk risk) => risk == PlatformRisk.High ? w.RiskHigh : w.RiskMedium;

    private static bool IsPolish(string language) =>
        language.Equals("pl", StringComparison.OrdinalIgnoreCase) || language.StartsWith("pl-", StringComparison.OrdinalIgnoreCase);

    private static string DroppedLine(Words w, IReadOnlyList<DroppedTile> dropped) =>
        $"{w.Dropped}: {dropped.Count} ({w.Codes}: {string.Join(", ", dropped.Select(d => d.ReasonCode).Distinct())})";

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
