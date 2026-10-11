"use client";

import { createContext, useContext } from "react";
import { DEFAULT_LOCALE, messagesFor, type Locale, type Messages } from "@/lib/messages";

// The request's language, decided on the server (lib/i18n.ts) and handed down once by the root layout, so client components
// render in the same language as the server ones. The catalogs are plain data bundled with the page; nothing is fetched.

const LocaleContext = createContext<Locale>(DEFAULT_LOCALE);

export function I18nProvider({ locale, children }: { locale: Locale; children: React.ReactNode }) {
  return <LocaleContext.Provider value={locale}>{children}</LocaleContext.Provider>;
}

export const useLocale = (): Locale => useContext(LocaleContext);

export function useMessages(): Messages {
  return messagesFor(useLocale());
}
