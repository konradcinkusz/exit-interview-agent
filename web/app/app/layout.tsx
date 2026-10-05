import type { Metadata } from "next";
import { headers } from "next/headers";
import { m } from "@/lib/messages";
import { SiteNav } from "./_components/SiteNav";
import "./globals.css";

export const metadata: Metadata = {
  title: { default: m.site.name, template: `%s · ${m.site.name}` },
  description: m.site.description,
};

export default async function RootLayout({ children }: { children: React.ReactNode }) {
  // Reading request headers makes every page dynamic. That is what lets Next stamp the per-request CSP nonce (set by the
  // edge gate, proxy.ts) on its own scripts; a statically prerendered page could not carry one (ADR-0047).
  await headers();
  return (
    <html lang="en">
      <body>
        <a className="skip" href="#main">{m.site.skipToContent}</a>
        <SiteNav />
        <main id="main" tabIndex={-1}>{children}</main>
        <footer className="site">{m.site.devBuild}</footer>
      </body>
    </html>
  );
}
