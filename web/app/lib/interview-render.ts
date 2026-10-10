import { interviewCopy } from "./messages/interview";
import type { InterviewLanguage, Tile, TileKind, Tiles } from "./interview-contract";

// Renders the drafts as one self-contained HTML file, after ExitInterviewAgent.Cli/Tiles/TileRenderer.cs: the same JSON, every
// value HTML-encoded, no script, no network resource, and the legal notice at the top. It runs in the browser from the JSON the
// page already holds, so the file exists only when the user asks for it and nothing is sent anywhere to make it.
// The copy button is not in the file: it needs a script, and the file is for keeping, not for publishing from.

const CSP = "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'";

const CSS = `:root { color-scheme: light dark; --bg: #f7f7f5; --fg: #1b1b1b; --muted: #4b4b4b; --card: #ffffff; --line: #b8b8b2; --note-bg: #fff4cc; --note-fg: #3b2a00; }
@media (prefers-color-scheme: dark) { :root { --bg: #141414; --fg: #f0f0f0; --muted: #c4c4c4; --card: #1e1e1e; --line: #4a4a4a; --note-bg: #2e2500; --note-fg: #ffe9a8; } }
body { margin: 0; padding: 16px; background: var(--bg); color: var(--fg); font: 16px/1.5 system-ui, sans-serif; }
h1 { font-size: 1.4rem; margin: 16px 0; }
.notice { background: var(--note-bg); color: var(--note-fg); border-left: 4px solid #b7791f; padding: 12px 14px; border-radius: 6px; }
.grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(100%, 260px), 1fr)); gap: 12px; }
.card { background: var(--card); border: 1px solid var(--line); border-radius: 8px; padding: 14px; }
.card.wide { grid-column: 1 / -1; }
.kind { color: var(--muted); font-size: .85rem; font-weight: 600; margin: 0 0 6px; }
.text { white-space: pre-line; overflow-wrap: anywhere; margin: 0; }
.meta { color: var(--muted); font-size: .85rem; }`;

const esc = (value: string) =>
  value.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&#39;");

export function tileLabel(kind: TileKind, language: InterviewLanguage): string {
  return interviewCopy[language].kinds[kind];
}

/** The text a copy button puts on the clipboard: the tile alone, without its label or the notice. */
export function tileCopyText(tile: Tile): string {
  return tile.text;
}

export function tilesToHtml(tiles: Tiles, language: InterviewLanguage): string {
  const copy = interviewCopy[language];
  const cards = tiles.items
    .map((tile) => {
      const wide = tile.kind === "reddit" ? " wide" : "";
      return `<article class="card${wide}">\n<p class="kind">${esc(tileLabel(tile.kind, language))}</p>\n<p class="text">${esc(tile.text)}</p>\n</article>`;
    })
    .join("\n");
  const body = tiles.items.length === 0 ? `<p>${esc(copy.tilesEmpty)}</p>` : `<div class="grid">\n${cards}\n</div>`;
  const dropped = tiles.dropped.length > 0 ? `<p class="meta">${esc(copy.dropped(tiles.dropped.length))}</p>` : "";
  return [
    "<!doctype html>",
    `<html lang="${esc(language)}">`,
    "<head>",
    '<meta charset="utf-8">',
    '<meta name="viewport" content="width=device-width, initial-scale=1">',
    `<meta http-equiv="Content-Security-Policy" content="${CSP}">`,
    `<title>${esc(copy.tilesTitle)}</title>`,
    `<style>\n${CSS}\n</style>`,
    "</head>",
    "<body>",
    `<header><p class="notice" role="note">${esc(tiles.notice)}</p><h1>${esc(copy.tilesTitle)}</h1>`,
    `<p class="meta">${esc(copy.draftWarning)}</p></header>`,
    `<main>\n${body}\n${dropped}\n</main>`,
    "</body>",
    "</html>",
    "",
  ].join("\n");
}

/** The drafts as JSON, the same shape the service sent, for keeping or for another tool. */
export function tilesToJson(tiles: Tiles): string {
  return JSON.stringify(tiles, null, 2) + "\n";
}
