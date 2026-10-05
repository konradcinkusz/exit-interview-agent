import { en, type Messages } from "./en";

// One catalog per language, all typed as `Messages`, so a missing key in a new language is a compile error. English is the
// only shipped language. To add Polish: create `pl.ts` exporting `const pl: Messages`, register it here, and have the root
// layout pick the locale from the request. Do it only when the whole catalog can be translated and reviewed.

export type Locale = "en";
export const DEFAULT_LOCALE: Locale = "en";

const catalogs: Record<Locale, Messages> = { en };

export function messagesFor(locale: Locale = DEFAULT_LOCALE): Messages {
  return catalogs[locale];
}

export const m: Messages = messagesFor();
export type { Messages };
