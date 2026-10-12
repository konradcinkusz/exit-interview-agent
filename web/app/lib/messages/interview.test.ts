import { describe, expect, it } from "vitest";
import { interviewCopy, type InterviewCopy } from "./interview";

// The types make the Polish catalog have the English keys. These tests cover what types cannot: every text is filled in, and the
// facts the consent states are said in both languages, so a rewording cannot quietly drop one of them.

function strings(value: unknown): string[] {
  if (typeof value === "string") return [value];
  if (typeof value === "function") return [];
  if (typeof value === "object" && value !== null) return Object.values(value).flatMap(strings);
  return [];
}

describe("interview copy", () => {
  it.each(["en", "pl"] as const)("has no empty text in %s", (language) => {
    const all = strings(interviewCopy[language]);
    expect(all.length).toBeGreaterThan(80);
    expect(all.filter((s) => s.trim() === "")).toEqual([]);
  });

  it.each(["en", "pl"] as const)("states the consent facts in %s", (language) => {
    const text = interviewCopy[language].consentPoints.join("\n");
    expect(text).toContain("Anthropic");
    expect(text).toMatch(/30 min/);
    expect(text).toMatch(/memory|pamięci/);
  });

  it("says the drafts are not facts, in both languages", () => {
    expect(interviewCopy.en.draftWarning).toMatch(/not facts/);
    expect(interviewCopy.pl.draftWarning).toMatch(/nie fakty/);
  });

  it.each(["en", "pl"] as const)("says a gone interview is gone in one way, for 404 and 410, in %s", (language) => {
    const errors = interviewCopy[language].errors;
    expect(errors.not_found).toBe(errors.gone);
    expect(errors.gone).toMatch(/no longer exists|już nie istnieje/);
  });

  it("asks a person with an unconfirmed address to confirm it, without blaming them", () => {
    expect(interviewCopy.en.errors.email_not_verified).toBe("To start an interview, confirm your email address first.");
    expect(interviewCopy.pl.errors.email_not_verified).toBe("Aby zacząć rozmowę, najpierw potwierdź adres e-mail.");
    expect(interviewCopy.en.confirmEmail).toBe("Confirm your email address");
    expect(interviewCopy.pl.confirmEmail).toBe("Potwierdź adres e-mail");
  });

  it("counts characters and wait times as sentences, not as markup", () => {
    const copy: InterviewCopy = interviewCopy.en;
    expect(copy.charCount(5, 2000)).toBe("5 of 2000 characters");
    expect(copy.errors.rate_limited(30)).toContain("30 seconds");
    expect(copy.errors.rate_limited()).not.toMatch(/\d/);
  });
});

describe("interview copy for the redesigned page (phase 2)", () => {
  it("says how many interviews are ready, with Polish plural forms", () => {
    expect(interviewCopy.en.ready(1)).toBe("1 interview ready");
    expect(interviewCopy.en.ready(3)).toBe("3 interviews ready");
    expect([1, 2, 4, 5, 12, 22, 25].map((n) => interviewCopy.pl.ready(n))).toEqual([
      "1 rozmowa gotowa",
      "2 rozmowy gotowe",
      "4 rozmowy gotowe",
      "5 rozmów gotowych",
      "12 rozmów gotowych",
      "22 rozmowy gotowe",
      "25 rozmów gotowych",
    ]);
  });

  it.each(["en", "pl"] as const)("puts the three facts that matter first in %s: an AI, Anthropic, 30 minutes", (language) => {
    const [ai, provider, kept] = interviewCopy[language].consentKey;
    expect(ai).toMatch(/AI/);
    expect(provider).toContain("Anthropic");
    expect(kept).toMatch(/30 min/);
  });

  it("finishes with a sentence the interviewer understands as a request to finish, not a withdrawal", () => {
    // Mirrors FinishRequestCue in src/ExitInterviewAgent.Agent/Machine/ReplySignals.cs (the parts these sentences rely on).
    const finishCue = /\b(mo[żz]emy|czy\s+mo[żz]emy)\s+(ju[żz]\s+)?(sko[ńn]czy[ćc])|\bcan\s+we\s+(please\s+)?(finish)\b/i;
    expect(interviewCopy.en.finishMessage).toMatch(finishCue);
    expect(interviewCopy.pl.finishMessage).toMatch(finishCue);
    for (const language of ["en", "pl"] as const) expect(interviewCopy[language].finishMessage).not.toMatch(/\bstop\b|przerw|wycof/i);
  });

  it.each(["en", "pl"] as const)("explains in %s what happens to the credit after a deletion", (language) => {
    expect(interviewCopy[language].deletedCredit).toMatch(/credit|kredyt/);
  });
});

describe("interview copy for the phase line and the consent quick answers (WP7)", () => {
  it("sends exactly the agreed quick answers, in each language", () => {
    expect(interviewCopy.en.quickReplies).toEqual(["Yes", "No"]);
    expect(interviewCopy.pl.quickReplies).toEqual(["Tak", "Tak, per Pani", "Tak, per Pan", "Tak, na Ty", "Nie"]);
  });

  it.each(["en", "pl"] as const)("gives the phase line in %s without counting topics", (language) => {
    const phase = interviewCopy[language].phase;
    expect(phase).not.toMatch(/\d+ (of|z) (about |około )?\d+/);
    expect(phase).not.toMatch(/topic|temat/i);
  });

  it("gives the phase line the agreed wording", () => {
    expect(interviewCopy.en.phase).toBe("Interview · usually 10–15 minutes · you can finish at any time");
    expect(interviewCopy.pl.phase).toBe("Rozmowa · zwykle 10–15 minut · można zakończyć w każdej chwili");
  });
});
