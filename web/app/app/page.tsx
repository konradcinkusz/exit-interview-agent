import Link from "next/link";
import { getMessages } from "@/lib/i18n";
import { hasSessionCookie } from "@/lib/session-hint";
import { HeroArt } from "./_components/HeroArt";
import { Icon, type IconName } from "./_components/Icon";
import { Stepper } from "./_components/Stepper";

const TRUST_ICONS: IconName[] = ["globe", "lock", "shield"];

/**
 * The start page, written for a former employee: one headline, one primary button, how it works in three steps, and what happens to
 * their words in three cards. The open-source description is on /about and the developer pages are behind the footer.
 */
export default async function Home() {
  const [{ t }, signedIn] = await Promise.all([getMessages(), hasSessionCookie()]);
  const L = t.landing;
  return (
    <>
      <section className="hero" aria-labelledby="home-heading">
        <div className="hero-text">
          <p className="eyebrow">{L.eyebrow}</p>
          <h1 id="home-heading">{L.title}</h1>
          <p className="lead">{L.lead}</p>
          <div className="actions">
            {/* Signed out, the same button goes to sign-in first (which links to sign-up), and comes back to the interview. */}
            <Link className="button primary large" href={signedIn ? "/interview" : "/login?redirect=%2Finterview"} prefetch={false} data-testid="interview-link">
              {L.start}
              <Icon name="arrow" size={20} />
            </Link>
          </div>
          {!signedIn && <p className="aside">{L.startNote}</p>}
        </div>
        <HeroArt />
      </section>

      <section className="section" aria-labelledby="journey-heading">
        <h2 id="journey-heading">{L.journeyTitle}</h2>
        <Stepper label={t.journey.label} steps={t.journey.steps} words={t.journey} />
      </section>

      <section className="section" aria-labelledby="trust-heading" data-testid="home-kept">
        <h2 id="trust-heading">{L.trustTitle}</h2>
        <ul className="trust-cards">
          {L.trust.map((card, i) => (
            <li key={card.title} className="card trust-card">
              <span className="icon-badge"><Icon name={TRUST_ICONS[i]} size={22} /></span>
              <h3>{card.title}</h3>
              <p>{card.text}</p>
            </li>
          ))}
        </ul>
        <p><Link href="/privacy">{L.keptLink}</Link></p>
      </section>

      <section className="section" aria-labelledby="not-heading">
        <h2 id="not-heading">{L.notTitle}</h2>
        <ul className="not-list">
          {L.not.map((s) => (
            <li key={s}><Icon name="info" size={18} />{s}</li>
          ))}
        </ul>
        <p><Link href="/about">{L.aboutLink}</Link></p>
      </section>
    </>
  );
}
