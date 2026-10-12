import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { draftsEn, draftsPl, glassdoorDraftEn, goWorkDraftPl } from "@/lib/interview-drafts.fixtures";
import { interviewCopy } from "@/lib/messages/interview";
import { PlatformDrafts } from "./PlatformDrafts";

// The cards are rendered to markup (this suite has no DOM). What the markup must hold: the link attributes, the risk in words,
// the warning as a note in the draft's language, the copy buttons disabled for a high-risk draft until the box is ticked (the
// tick itself is in the browser suite), and no Google card. Synthetic fixtures in both languages.

const render = (drafts: typeof draftsEn, language: "en" | "pl") =>
  renderToStaticMarkup(<PlatformDrafts copy={interviewCopy[language]} language={language} drafts={drafts} />);

/** The opening tag of the element named `tag` that holds `fragment` (an attribute or the text inside it). */
const tagWith = (html: string, fragment: string, tag: "a" | "button" | "p" = "button") => {
  const at = html.indexOf(fragment);
  if (at < 0) return "";
  const start = html.lastIndexOf(`<${tag} `, at);
  return html.slice(start, html.indexOf(">", start) + 1);
};

describe("PlatformDrafts", () => {
  it("renders nothing when there are no drafts", () => {
    expect(render([], "en")).toBe("");
  });

  it("has a heading, an intro and one card per draft, in the interview's language", () => {
    const en = render(draftsEn, "en");
    expect(en).toContain("<h3");
    expect(en).toContain("Drafts for a platform");
    expect(en).toContain('lang="en"');
    const pl = render(draftsPl, "pl");
    expect(pl).toContain("Szkice dla serwisów");
    expect(pl.match(/<article/g)).toHaveLength(draftsPl.length);
  });

  it("opens the site in a new tab with no query string, and says so in the link", () => {
    const html = render([goWorkDraftPl], "pl");
    const link = tagWith(html, "Otwórz GoWork.pl", "a");
    expect(link).toContain('href="https://www.gowork.pl/"');
    expect(link).toContain('target="_blank"');
    expect(link).toContain('rel="noopener noreferrer"');
    expect(link).not.toContain("?");
    expect(html).toContain("(otwiera nową kartę)");
  });

  it("states the risk in words, not only by colour", () => {
    expect(render([goWorkDraftPl], "pl")).toContain("Wyższe ryzyko");
    expect(render([glassdoorDraftEn], "en")).toContain("Medium risk");
    expect(render([glassdoorDraftEn], "en")).not.toContain("Higher risk");
  });

  it("shows the warning as a note in the draft's language", () => {
    const html = render([goWorkDraftPl], "pl");
    expect(tagWith(html, "To szkic, nie fakt.", "p")).toMatch(/role="note"/);
    expect(tagWith(html, "To szkic, nie fakt.", "p")).toMatch(/lang="pl"/);
  });

  it("gives each section its own copy button, named with the draft and the section", () => {
    const html = render([goWorkDraftPl], "pl");
    expect(html).toContain('aria-label="Kopiuj: GoWork.pl, Plusy"');
    expect(html).toContain('aria-label="Kopiuj: GoWork.pl, Minusy"');
    expect(html).toContain("Kopiuj całość");
  });

  it("names a section with no label after its draft", () => {
    expect(render([draftsEn[1]], "en")).toContain('aria-label="Copy: LinkedIn"');
  });

  it("lists the fields to fill in on the site", () => {
    const html = render([glassdoorDraftEn], "en");
    expect(html).toContain("You fill in on the site:");
    expect(html).toContain("Star ratings");
  });

  it("disables every copy button of a high-risk draft until the warning is acknowledged", () => {
    const html = render([goWorkDraftPl], "pl");
    expect(tagWith(html, 'aria-label="Kopiuj: GoWork.pl, Plusy"')).toContain("disabled");
    expect(tagWith(html, 'aria-label="Kopiuj: GoWork.pl, Minusy"')).toContain("disabled");
    expect(tagWith(html, "Kopiuj całość")).toContain("disabled");
    expect(html).toContain("Przeczytałem/am to ostrzeżenie");
  });

  it("leaves a medium-risk draft copyable, with no acknowledgement box", () => {
    const html = render([glassdoorDraftEn], "en");
    expect(tagWith(html, 'aria-label="Copy: Glassdoor, Pros"')).not.toContain("disabled");
    expect(tagWith(html, "Copy all")).not.toContain("disabled");
    expect(html).not.toContain("I have read this warning");
  });

  it("carries no Google card and no script", () => {
    const html = render([...draftsEn, ...draftsPl], "en");
    expect(html).not.toMatch(/google/i);
    expect(html).not.toMatch(/<script/i);
  });
});
