import { describe, expect, it } from "vitest";
import type { Tiles } from "./interview-contract";
import { tileCopyText, tileLabel, tilesToHtml, tilesToJson } from "./interview-render";

// The downloadable tiles.html is rendered in the browser from the same JSON the page shows, after the TileRenderer.cs pattern:
// every value HTML-encoded, no script, no network resource, the notice on the page (ADR-0047 spirit, applied to a file the user saves).

const tiles: Tiles = {
  items: [
    { kind: "glassdoor", text: "Good onboarding. <img src=x onerror=alert(1)>" },
    { kind: "reddit", text: "Line one\nLine two" },
    { kind: "google_review", text: "Fair pay & clear goals." },
  ],
  dropped: [{ code: "too_long" }],
  notice: "These are draft texts, not facts. This is not legal advice.",
};

describe("tilesToHtml", () => {
  it("renders one card per tile, in order, with its kind label", () => {
    const html = tilesToHtml(tiles, "en");
    const glassdoor = html.indexOf("Glassdoor");
    const reddit = html.indexOf("Reddit");
    const google = html.indexOf("Google review");
    expect(glassdoor).toBeGreaterThan(-1);
    expect(reddit).toBeGreaterThan(glassdoor);
    expect(google).toBeGreaterThan(reddit);
    expect(html.match(/<article/g)).toHaveLength(3);
  });

  it("encodes every tile text: no markup from the interview ever becomes markup", () => {
    const html = tilesToHtml(tiles, "en");
    expect(html).not.toContain("<img");
    expect(html).toContain("&lt;img src=x onerror=alert(1)&gt;");
    expect(html).toContain("Fair pay &amp; clear goals.");
  });

  it("has no script, no network resource and a restrictive Content-Security-Policy", () => {
    const html = tilesToHtml(tiles, "en");
    expect(html).not.toMatch(/<script/i);
    expect(html).not.toMatch(/<[^>]*\bsrc=/i); // the escaped "src=x" inside a tile is text, not an attribute
    expect(html).not.toMatch(/https?:\/\//i);
    expect(html).toMatch(/http-equiv="Content-Security-Policy" content="default-src 'none'/);
  });

  it("shows the legal notice and the dropped count", () => {
    const html = tilesToHtml(tiles, "en");
    expect(html).toContain("These are draft texts, not facts. This is not legal advice.");
    expect(html).toContain("Texts left out: 1.");
  });

  it("uses the interview language for labels and the page language", () => {
    const html = tilesToHtml(tiles, "pl");
    expect(html).toContain('<html lang="pl">');
    expect(html).toContain("Opinia Google");
    expect(html).toContain("Szkice tekstów");
  });

  it("says so when there are no tiles", () => {
    expect(tilesToHtml({ ...tiles, items: [] }, "en")).toContain("No draft texts were made from this interview.");
  });
});

describe("tileLabel", () => {
  it("has a label for every kind in both languages", () => {
    for (const kind of ["glassdoor", "google_review", "reddit", "short_note", "overview", "facts"] as const) {
      expect(tileLabel(kind, "en")).not.toBe("");
      expect(tileLabel(kind, "pl")).not.toBe("");
    }
    expect(tileLabel("google_review", "pl")).not.toBe(tileLabel("google_review", "en"));
  });
});

describe("tileCopyText and tilesToJson", () => {
  it("copies the tile text alone, without the label or the notice", () => {
    expect(tileCopyText(tiles.items[1])).toBe("Line one\nLine two");
  });

  it("serialises the same JSON the page shows", () => {
    expect(JSON.parse(tilesToJson(tiles))).toEqual(tiles);
  });
});
