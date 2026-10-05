import type { Metadata } from "next";
import { m } from "@/lib/messages";

const P = m.privacy;
export const metadata: Metadata = { title: P.title };

export default function PrivacyPage() {
  return (
    <>
      <h1>{P.title}</h1>
      <p>{P.lead}</p>
      <h2>{P.factsTitle}</h2>
      <ul className="plain" data-testid="privacy-facts">{P.facts.map((f) => <li key={f}>{f}</li>)}</ul>
      <h2>{P.limitsTitle}</h2>
      <ul className="plain">{P.limits.map((f) => <li key={f}>{f}</li>)}</ul>
      <h2>{P.linksTitle}</h2>
      <ul className="plain">
        {P.links.map((l) => <li key={l.href}><a href={l.href} rel="noopener noreferrer">{l.label}</a></li>)}
      </ul>
      <p className="muted">{P.linksNote}</p>
    </>
  );
}
