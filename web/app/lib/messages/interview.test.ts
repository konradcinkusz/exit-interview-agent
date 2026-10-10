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
    expect(interviewCopy.en.errors.email_not_verified).toBe("To start an interview, confirm your email address in your account settings.");
    expect(interviewCopy.pl.errors.email_not_verified).toBe("Aby zacząć wywiad, potwierdź adres e-mail w ustawieniach konta.");
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
