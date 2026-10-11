import type { Metadata } from "next";
import { getMessages } from "@/lib/i18n";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getMessages()).t.privacy.title };
}

export default async function PrivacyPage() {
  const { t } = await getMessages();
  const P = t.privacy;
  return (
    <>
      <h1>{P.title}</h1>
      <p className="lead">{P.lead}</p>
      <h2>{P.interviewTitle}</h2>
      <ul className="plain" data-testid="privacy-interview">{P.interview.map((f) => <li key={f}>{f}</li>)}</ul>
      <h2>{P.factsTitle}</h2>
      <ul className="plain" data-testid="privacy-facts">{P.facts.map((f) => <li key={f}>{f}</li>)}</ul>
      <h2>{P.limitsTitle}</h2>
      <ul className="plain">{P.limits.map((f) => <li key={f}>{f}</li>)}</ul>
      <h2>{P.cookiesTitle}</h2>
      <p>{P.cookies}</p>
      <h2>{P.linksTitle}</h2>
      <ul className="plain">
        {P.links.map((l) => <li key={l.href}><a href={l.href} rel="noopener noreferrer">{l.label}</a></li>)}
      </ul>
      <p className="hint">{P.linksNote}</p>
    </>
  );
}
