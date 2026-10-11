import Link from "next/link";
import { getMessages } from "@/lib/i18n";
import { hasSessionCookie } from "@/lib/session-hint";

/**
 * The start page, written for a former employee: one headline, one primary button, what is kept. The open-source description is
 * on /about and the developer pages are behind the footer.
 */
export default async function Home() {
  const [{ t }, signedIn] = await Promise.all([getMessages(), hasSessionCookie()]);
  const L = t.landing;
  return (
    <>
      <section className="hero" aria-labelledby="home-heading">
        <h1 id="home-heading">{L.title}</h1>
        <p className="lead">{L.lead}</p>
        <div className="actions">
          {/* Signed out, the same button goes to sign-in first (which links to sign-up), and comes back to the interview. */}
          <Link className="button primary large" href={signedIn ? "/interview" : "/login?redirect=%2Finterview"} prefetch={false} data-testid="interview-link">
            {L.start}
          </Link>
          {!signedIn && <p className="aside">{L.startNote}</p>}
        </div>
      </section>

      <section className="card" aria-labelledby="kept-heading">
        <h2 id="kept-heading">{L.keptTitle}</h2>
        <p data-testid="home-kept">{L.kept}</p>
        <p><Link href="/privacy">{L.keptLink}</Link></p>
      </section>

      <h2>{L.stepsTitle}</h2>
      <ol className="steps">{L.steps.map((s) => <li key={s}>{s}</li>)}</ol>

      <h2>{L.notTitle}</h2>
      <ul className="plain">{L.not.map((s) => <li key={s}>{s}</li>)}</ul>
      <p><Link href="/about">{L.aboutLink}</Link></p>
    </>
  );
}
