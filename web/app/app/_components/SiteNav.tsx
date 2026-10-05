"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { m } from "@/lib/messages";

// `gated` links are never prefetched: a prefetch made while signed out is answered with the edge gate's redirect to /login,
// and the router would replay that cached redirect after the user signs in, bouncing them back to the sign-in page.
const LINKS = [
  { href: "/", label: m.nav.home, gated: false },
  { href: "/connect", label: m.nav.connect, gated: false },
  { href: "/cli", label: m.nav.cli, gated: true },
  { href: "/delete-submission", label: m.nav.deleteSubmission, gated: false },
  { href: "/privacy", label: m.nav.privacy, gated: false },
  { href: "/account", label: m.nav.account, gated: true },
];

/** One header on every page. It does not know whether the visitor is signed in: gated pages send them to sign in and back. */
export function SiteNav() {
  const pathname = usePathname();
  return (
    <header className="site">
      <div className="inner">
        <Link className="brand" href="/">{m.site.name}</Link>
        <nav aria-label={m.nav.label}>
          <ul>
            {LINKS.map((l) => (
              <li key={l.href}>
                <Link href={l.href} prefetch={l.gated ? false : undefined} aria-current={pathname === l.href ? "page" : undefined}>{l.label}</Link>
              </li>
            ))}
          </ul>
        </nav>
      </div>
    </header>
  );
}
