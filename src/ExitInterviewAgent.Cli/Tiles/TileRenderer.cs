using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExitInterviewAgent.Agent.Tiles;

namespace ExitInterviewAgent.Cli.Tiles;

/// <summary>
/// Shows a <see cref="TileSet"/> three ways: numbered plain text for the terminal, one self-contained HTML page, and camelCase
/// JSON (<c>tiles.json</c>). Every value that comes from the set is HTML-encoded. The page loads nothing from the network (no
/// script, font, image or stylesheet link) and runs one inline script whose SHA-256 hash is in its Content-Security-Policy.
/// The fixed notice is the only wording here that is not from the set; it is defined once, in <see cref="NoticePl"/> and
/// <see cref="NoticeEn"/>.
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
        .dropped { margin-top: 16px; }
        :focus-visible { outline: 3px solid #d97706; outline-offset: 2px; }
        """;

    /// <summary>Terminal and page wording by language. Anything not Polish is shown in English.</summary>
    private sealed record Words(string Title, string NoTiles, string BasedOn, string Dropped, string Codes, string Copy, string Copied, string Version, string Notice, string[] KindLabels)
    {
        /// <summary>Kind labels, indexed by <see cref="TileKind"/>.</summary>
        public static readonly Words Pl = new("Propozycje tekstów", "Brak kafelków.", "na podstawie", "Odrzucone", "kody", "Kopiuj", "Skopiowano", "wersja", NoticePl,
            ["Fakty", "Przegląd", "Co się sprawdziło", "Co można poprawić", "Dla następnej osoby", "Krótka notatka", "Glassdoor", "Opinia Google", "Reddit"]);

        public static readonly Words En = new("Draft texts", "No tiles.", "based on", "Dropped", "codes", "Copy", "Copied", "version", NoticeEn,
            ["Facts", "Overview", "What worked", "What could improve", "For the next person", "Short note", "Glassdoor", "Google review", "Reddit"]);

        public static Words For(string language) =>
            language.Equals("pl", StringComparison.OrdinalIgnoreCase) || language.StartsWith("pl-", StringComparison.OrdinalIgnoreCase) ? Pl : En;
    }

    /// <summary>Plain text for the terminal: numbered blocks, the dropped codes, and the notice last.</summary>
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
        if (set.Dropped.Count > 0) text.Append(DroppedLine(w, set.Dropped)).Append("\n\n");
        text.Append(w.Notice).Append('\n');
        return text.ToString();
    }

    /// <summary>One self-contained page: notice at the top, a card per tile with a copy button, no network resources.</summary>
    public static string ToHtml(TileSet set)
    {
        var w = Words.For(set.Language);
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
            var wide = tile.Kind == TileKind.Reddit ? " wide" : string.Empty;
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
        if (set.Dropped.Count > 0) html.Append("<p class=\"meta dropped\">").Append(E(DroppedLine(w, set.Dropped))).Append("</p>\n");
        html.Append("</main>\n<footer><p class=\"meta\">").Append(E(w.Version)).Append(' ').Append(E(set.TilesVersion)).Append("</p></footer>\n")
            .Append("<script>").Append(CopyScript).Append("</script>\n")
            .Append("</body>\n</html>\n");
        return html.ToString();
    }

    /// <summary>The set as camelCase JSON, the content of <c>tiles.json</c>. The notice is not part of it; the text and HTML carry it.</summary>
    public static string ToJson(TileSet set) => JsonSerializer.Serialize(set, JsonOptions);

    private static string DroppedLine(Words w, IReadOnlyList<DroppedTile> dropped) =>
        $"{w.Dropped}: {dropped.Count} ({w.Codes}: {string.Join(", ", dropped.Select(d => d.ReasonCode).Distinct())})";

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
