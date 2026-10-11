"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

/**
 * A header link that marks the current page. `gated` links are never prefetched: a prefetch made while signed out is answered with
 * the edge gate's redirect to /login, and the router would replay that cached redirect after sign-in, bouncing the person back.
 */
export function NavLink({ href, gated, match, children }: { href: string; gated?: boolean; match?: string; children: React.ReactNode }) {
  const pathname = usePathname();
  const current = pathname === (match ?? href);
  return (
    <Link href={href} prefetch={gated ? false : undefined} aria-current={current ? "page" : undefined}>
      {children}
    </Link>
  );
}
