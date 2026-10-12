"use client";

import { useRouter } from "next/navigation";
import { LOCALE_COOKIE, LOCALE_COOKIE_MAX_AGE, isLocale } from "@/lib/messages";
import { useMessages } from "./I18n";
import { Icon } from "./Icon";

/**
 * Switches the portal to the other language. The choice is a preference cookie (no secret, so not HttpOnly) that the server reads
 * on every request; a refresh re-renders the page in the new language without losing what the page holds in memory.
 */
export function LanguageSwitch() {
  const router = useRouter();
  const t = useMessages();
  const target = t.language.switchToLang;
  function change() {
    if (!isLocale(target)) return;
    const secure = window.location.protocol === "https:" ? "; Secure" : "";
    document.cookie = `${LOCALE_COOKIE}=${target}; Path=/; Max-Age=${LOCALE_COOKIE_MAX_AGE}; SameSite=Lax${secure}`;
    router.refresh();
  }
  return (
    <button type="button" className="linklike lang-switch" lang={target} title={t.language.switchHint} onClick={change} data-testid="language-switch">
      <Icon name="globe" size={16} />
      {t.language.switchTo}
    </button>
  );
}
