import { cookies, headers } from "next/headers";
import { LOCALE_COOKIE, messagesFor, resolveLocale, type Locale, type Messages } from "./messages";

/** The language of this request (cookie, then Accept-Language, then English). Server components and route handlers only. */
export async function getLocale(): Promise<Locale> {
  const [jar, hdrs] = await Promise.all([cookies(), headers()]);
  return resolveLocale(jar.get(LOCALE_COOKIE)?.value, hdrs.get("accept-language"));
}

export async function getMessages(): Promise<{ locale: Locale; t: Messages }> {
  const locale = await getLocale();
  return { locale, t: messagesFor(locale) };
}
