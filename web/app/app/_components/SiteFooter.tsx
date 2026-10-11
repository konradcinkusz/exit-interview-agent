import Link from "next/link";
import { getMessages } from "@/lib/i18n";

/** Everything a former employee does not need every day: privacy, the project, the developer pages, and the build notice, once. */
export async function SiteFooter() {
  const { t } = await getMessages();
  return (
    <footer className="site-footer">
      <div className="site-footer__inner">
        <nav aria-label={t.footer.label}>
          <ul className="footer-links">
            <li><Link href="/privacy">{t.footer.privacy}</Link></li>
            <li><Link href="/about">{t.footer.about}</Link></li>
            <li><Link href="/developers">{t.footer.developers}</Link></li>
          </ul>
        </nav>
        <p className="dev-notice" data-testid="dev-notice">{t.site.devBuild}</p>
      </div>
    </footer>
  );
}
