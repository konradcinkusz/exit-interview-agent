import type { Metadata } from "next";
import { headers } from "next/headers";
import { getMessages } from "@/lib/i18n";
import { HydrationMarker } from "./_components/HydrationMarker";
import { I18nProvider } from "./_components/I18n";
import { SiteFooter } from "./_components/SiteFooter";
import { SiteHeader } from "./_components/SiteHeader";
import "./globals.css";

export async function generateMetadata(): Promise<Metadata> {
  const { t } = await getMessages();
  return { title: { default: t.site.name, template: `%s · ${t.site.name}` }, description: t.site.description };
}

export default async function RootLayout({ children }: { children: React.ReactNode }) {
  // Reading request headers makes every page dynamic. That is what lets Next stamp the per-request CSP nonce (set by the
  // edge gate, proxy.ts) on its own scripts; a statically prerendered page could not carry one (ADR-0047). The language is
  // per request too: the switch's cookie, then the browser's Accept-Language (lib/i18n.ts).
  await headers();
  const { locale, t } = await getMessages();
  return (
    <html lang={locale}>
      <body>
        <I18nProvider locale={locale}>
          <a className="skip" href="#main">{t.site.skipToContent}</a>
          <SiteHeader />
          <main id="main" tabIndex={-1}>{children}</main>
          <SiteFooter />
          <HydrationMarker />
        </I18nProvider>
      </body>
    </html>
  );
}
