import { developer } from "./developer";
import { en, type Messages } from "./en";
import { pl } from "./pl";

// One portal catalog per language, all typed as `Messages`, so a missing key in a language is a compile error. The language of a
// request is, in order: the `locale` cookie (set by the language switch in the header), then the browser's Accept-Language, then
// English (lib/i18n.ts reads both on the server; app/_components/I18n.tsx hands the result to client components).

export const LOCALES = ["en", "pl"] as const;
export type Locale = (typeof LOCALES)[number];
export const DEFAULT_LOCALE: Locale = "en";

/** The cookie the language switch writes. Not HttpOnly: it holds a preference the page itself sets, never a secret. */
export const LOCALE_COOKIE = "locale";
export const LOCALE_COOKIE_MAX_AGE = 60 * 60 * 24 * 365;

const catalogs: Record<Locale, Messages> = { en, pl };

export const isLocale = (value: unknown): value is Locale => typeof value === "string" && (LOCALES as readonly string[]).includes(value);

export function messagesFor(locale: Locale = DEFAULT_LOCALE): Messages {
  return catalogs[locale];
}

/**
 * The first supported language in an Accept-Language header, by the order and weights the browser gave ("pl-PL,pl;q=0.9,en;q=0.8"
 * is Polish). Only the primary subtag counts. Anything unparseable or unsupported is English.
 */
export function localeFromAcceptLanguage(header: string | null | undefined): Locale {
  if (!header) return DEFAULT_LOCALE;
  const ranked = header
    .split(",")
    .map((part, index) => {
      const [tag, ...params] = part.trim().split(";");
      const q = params.map((p) => /^\s*q=([0-9.]+)\s*$/.exec(p)?.[1]).find(Boolean);
      return { lang: tag.trim().toLowerCase().split("-")[0], q: q === undefined ? 1 : Number(q), index };
    })
    .filter((entry) => entry.lang && Number.isFinite(entry.q) && entry.q > 0)
    .sort((a, b) => b.q - a.q || a.index - b.index);
  return ranked.map((entry) => entry.lang).find(isLocale) ?? DEFAULT_LOCALE;
}

/** The cookie wins over the browser: the person chose it with the switch. */
export function resolveLocale(cookie: string | null | undefined, acceptLanguage: string | null | undefined): Locale {
  return isLocale(cookie) ? cookie : localeFromAcceptLanguage(acceptLanguage);
}

/** The developer pages' catalog (English only). Imported as `m` there, as it always was. */
export const devMessages = developer;
export type { Messages };
