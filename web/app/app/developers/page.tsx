import type { Metadata } from "next";
import Link from "next/link";
import { getMessages } from "@/lib/i18n";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getMessages()).t.developers.title };
}

/**
 * The pages a former employee does not need, in one place: connecting an AI client, the CLI ticket, deleting a submission by
 * receipt code, and the employer figures. Every route still works where it always was; only the header no longer lists them.
 */
export default async function DevelopersPage() {
  const { locale, t } = await getMessages();
  const D = t.developers;
  return (
    <>
      <h1>{D.title}</h1>
      <p className="lead">{D.lead}</p>
      {locale !== "en" && <p className="notice">{D.englishOnly}</p>}
      <ul className="link-cards" data-testid="developer-links">
        {D.links.map((l) => (
          <li key={l.href} className="card">
            <Link href={l.href} prefetch={l.gated ? false : undefined} lang="en" className="card-title">{l.label}</Link>
            <p>{l.what}</p>
          </li>
        ))}
      </ul>
    </>
  );
}
