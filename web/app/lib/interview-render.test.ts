import { describe, expect, it } from "vitest";
import type { Tiles } from "./interview-contract";
import { interviewCopy } from "./messages/interview";
import { draftsEn, draftsPl, glassdoorDraftEn, goWorkDraftPl } from "./interview-drafts.fixtures";
import { tileCopyText, tileLabel, tilesToHtml, tilesToJson } from "./interview-render";

// The downloadable tiles.html is rendered in the browser from the same JSON the page shows, after the TileRenderer.cs pattern:
// every value HTML-encoded, no script, no network resource, the notice on the page (ADR-0047 spirit, applied to a file the user saves).

const tiles: Tiles = {
  items: [
    { kind: "overview", text: "Good onboarding. <img src=x onerror=alert(1)>" },
    { kind: "what_worked", text: "Line one\nLine two" },
    { kind: "what_could_improve", text: "Fair pay & clear goals." },
  ],
  dropped: [{ code: "too_long" }],
  notice: "These are draft texts, not facts. This is not legal advice.",
  drafts: [],
};

describe("tilesToHtml", () => {
  it("renders one card per tile, in order, with its kind label", () => {
    const html = tilesToHtml(tiles, "en");
    const overview = html.indexOf("Overview");
    const worked = html.indexOf("What worked");
    const improve = html.indexOf("What could improve");
    expect(overview).toBeGreaterThan(-1);
    expect(worked).toBeGreaterThan(overview);
    expect(improve).toBeGreaterThan(worked);
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
    expect(html).toContain("1 draft text was left out because it failed an automatic check");
  });

  it("uses the interview language for labels and the page language", () => {
    const html = tilesToHtml(tiles, "pl");
    expect(html).toContain('<html lang="pl">');
    expect(html).toContain("Co można poprawić");
    expect(html).toContain("Szkice tekstów");
  });

  it("says so when there are no tiles", () => {
    expect(tilesToHtml({ ...tiles, items: [] }, "en")).toContain("No draft texts were made from this interview.");
  });
});

describe("tileLabel", () => {
  it("has a label for every kind in both languages", () => {
    for (const kind of ["facts", "overview", "what_worked", "what_could_improve", "for_the_next_person"] as const) {
      expect(tileLabel(kind, "en")).not.toBe("");
      expect(tileLabel(kind, "pl")).not.toBe("");
    }
    expect(tileLabel("what_could_improve", "pl")).not.toBe(tileLabel("what_could_improve", "en"));
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

// Platform drafts (platform-drafts-spec §3.5): the saved page carries each draft with its warning, its sections with their labels,
// the fields the person fills in on the site, and the site as plain text. The saved file never links out (its CSP has no network).
describe("tilesToHtml: platform drafts", () => {
  const generic: Tiles = { ...tiles, items: [{ kind: "overview", text: "A generic overview." }] };

  it("renders one card per draft after the generic tiles, with the platform name and its risk in words", () => {
    const html = tilesToHtml({ ...generic, drafts: draftsEn }, "en");
    expect(html).toContain("<h2>Drafts for a platform</h2>");
    expect(html.indexOf("A generic overview.")).toBeLessThan(html.indexOf("Glassdoor"));
    expect(html.indexOf("Glassdoor")).toBeLessThan(html.indexOf("LinkedIn"));
    expect(html).toContain("Higher risk"); // LinkedIn
    expect(html).toContain("Medium risk"); // Glassdoor
  });

  it("shows each section with its own label and text, and the fields to fill in on the site", () => {
    const html = tilesToHtml({ ...generic, drafts: [glassdoorDraftEn] }, "en");
    expect(html).toContain("Headline");
    expect(html).toContain("Promotion criteria were never written down.");
    expect(html).toContain("You fill in on the site:");
    expect(html).toContain("Star ratings");
  });

  it("gives the warning of each draft, with the notice still at the top", () => {
    const html = tilesToHtml({ ...generic, drafts: draftsEn }, "en");
    expect(html).toContain("Draft, not fact. Not legal advice.");
    expect(html).toContain("These are draft texts, not facts. This is not legal advice.");
  });

  it("writes the Polish words, labels and warnings for a Polish result", () => {
    const html = tilesToHtml({ ...generic, drafts: draftsPl }, "pl");
    expect(html).toContain('<html lang="pl">');
    expect(html).toContain("Szkice dla serwisów");
    expect(html).toContain("Wyższe ryzyko"); // GoWork
    expect(html).toContain("Plusy");
    expect(html).toContain("Uzupełnisz w serwisie:");
    expect(html).toContain("To szkic, nie fakt. To nie jest porada prawna.");
  });

  it("encodes draft sections and labels: no markup from a draft ever becomes markup", () => {
    const html = tilesToHtml({ ...generic, drafts: [goWorkDraftPl] }, "pl");
    expect(html).not.toContain("<b>Nie</b>");
    expect(html).toContain("&lt;b&gt;Nie&lt;/b&gt; &amp; &quot;zawsze&quot;");
  });

  it("shows the site as plain text, never as a link, and keeps the no-network rule", () => {
    const html = tilesToHtml({ ...generic, drafts: draftsPl }, "pl");
    expect(html).toContain("www.gowork.pl");
    expect(html).not.toMatch(/href=/i);
    expect(html).not.toMatch(/https?:\/\//i);
  });

  it("has no drafts heading when there are no drafts", () => {
    expect(tilesToHtml({ ...generic, drafts: [] }, "en")).not.toContain("Drafts for a platform");
  });
});

describe("tilesToJson: platform drafts", () => {
  it("keeps the notice and the drafts, so the JSON file says what the page shows", () => {
    const parsed = JSON.parse(tilesToJson({ ...tiles, drafts: draftsEn })) as Tiles;
    expect(parsed.notice).toBe(tiles.notice);
    expect(parsed.drafts).toEqual(draftsEn);
  });
});

describe("generic kinds have labels in both languages", () => {
  it.each(["what_worked", "what_could_improve", "for_the_next_person"])("%s", (kind) => {
    expect(interviewCopy.en.kinds).toHaveProperty(kind);
    expect(interviewCopy.pl.kinds).toHaveProperty(kind);
    expect(interviewCopy.pl.kinds[kind as keyof typeof interviewCopy.pl.kinds]).not.toBe(interviewCopy.en.kinds[kind as keyof typeof interviewCopy.en.kinds]);
  });
});
