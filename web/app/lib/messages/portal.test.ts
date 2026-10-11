import { describe, expect, it } from "vitest";
import { en } from "./en";
import { LOCALES, localeFromAcceptLanguage, messagesFor, resolveLocale } from "./index";
import { pl } from "./pl";

// The types make the Polish catalog have the English keys. These tests cover what types cannot: every text is filled in, the
// languages really differ, the facts the pages state survive in both, and the request's language is chosen as documented.

function strings(value: unknown): string[] {
  if (typeof value === "string") return [value];
  if (typeof value === "object" && value !== null) return Object.values(value).flatMap(strings);
  return [];
}

describe("portal catalogs", () => {
  it.each(LOCALES)("has no empty text in %s", (locale) => {
    const all = strings(messagesFor(locale));
    expect(all.length).toBeGreaterThan(150);
    expect(all.filter((s) => s.trim() === "")).toEqual([]);
  });

  it("is really translated: the Polish catalog shares few sentences with the English one", () => {
    const english = new Set(strings(en));
    // Shared on purpose: the product name, link targets, and the names of the English-only developer pages.
    const pageNames = new Set<string>(en.developers.links.map((l) => l.label));
    const shared = strings(pl).filter((s) => english.has(s) && s.length > 12 && !pageNames.has(s));
    expect(shared.filter((s) => !/^(https:|\/|Exit Interview Agent$)/.test(s))).toEqual([]);
  });

  it.each(LOCALES)("says in %s that the conversation goes to Anthropic and that the result is wiped after 30 minutes", (locale) => {
    const t = messagesFor(locale);
    expect(t.landing.kept).toContain("Anthropic");
    expect(t.landing.kept).toMatch(/30 min/);
    expect(t.privacy.interview.join(" ")).toContain("Anthropic");
  });

  it("no longer tells a former employee the site is not for real people, and shows the build notice in one place", () => {
    for (const t of [en, pl]) {
      expect(strings(t.landing).join(" ")).not.toMatch(/not for real people|nie dla prawdziwych/i);
      expect(strings(t.landing).join(" ")).not.toContain(t.site.devBuild);
    }
  });

  it("never calls a record anonymous, in either language", () => {
    expect(strings(en).join(" ")).not.toMatch(/\banonymi[sz]ed\b/i);
    expect(strings(pl).join(" ")).not.toMatch(/\bzanonimizowan/i);
  });

  it("the language switch in each catalog names the other language", () => {
    expect(en.language.switchToLang).toBe("pl");
    expect(pl.language.switchToLang).toBe("en");
  });
});

describe("the request's language", () => {
  it.each([
    ["pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7", "pl"],
    ["en-GB,en;q=0.9,pl;q=0.8", "en"],
    ["de-DE,de;q=0.9,pl;q=0.8", "pl"],
    ["en;q=0.5,pl;q=0.9", "pl"],
    ["fr-FR", "en"],
    ["", "en"],
    [null, "en"],
    ["*", "en"],
    ["pl;q=0", "en"],
  ])("Accept-Language %j is %s", (header, expected) => {
    expect(localeFromAcceptLanguage(header)).toBe(expected);
  });

  it("the switch's cookie wins over the browser, and an unknown cookie value is ignored", () => {
    expect(resolveLocale("en", "pl-PL")).toBe("en");
    expect(resolveLocale("pl", "en-GB")).toBe("pl");
    expect(resolveLocale("de", "pl-PL")).toBe("pl");
    expect(resolveLocale(undefined, undefined)).toBe("en");
  });
});
