import Link from "next/link";
import { getMessages } from "@/lib/i18n";
import { hasSessionCookie } from "@/lib/session-hint";
import { LanguageSwitch } from "./LanguageSwitch";
import { NavLink } from "./NavLink";
import { SignOutButton } from "./SignOutButton";

/**
 * One header on every page, with only what a former employee needs: the interview, the account (or sign in), sign out and the
 * language. Whether a session cookie is present decides which items show; that is a display hint, not authorization (the edge
 * gate and the APIs decide access). The developer pages are linked from the footer.
 */
export async function SiteHeader() {
  const [{ t }, signedIn] = await Promise.all([getMessages(), hasSessionCookie()]);
  return (
    <header className="site-header">
      <div className="site-header__inner">
        <Link className="brand" href="/">{t.site.name}</Link>
        <nav aria-label={t.nav.label}>
          <ul className="site-nav">
            <li><NavLink href="/interview" gated>{t.nav.interview}</NavLink></li>
            {signedIn ? (
              <>
                <li><NavLink href="/account" gated>{t.nav.account}</NavLink></li>
                <li><SignOutButton label={t.nav.signOut} /></li>
              </>
            ) : (
              <li><NavLink href="/login" match="/login">{t.nav.signIn}</NavLink></li>
            )}
            <li><LanguageSwitch /></li>
          </ul>
        </nav>
      </div>
    </header>
  );
}
