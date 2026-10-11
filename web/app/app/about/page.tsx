import type { Metadata } from "next";
import Link from "next/link";
import { getMessages } from "@/lib/i18n";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getMessages()).t.about.title };
}

function List({ items, testId }: { items: readonly string[]; testId?: string }) {
  return <ul className="plain" data-testid={testId}>{items.map((i) => <li key={i}>{i}</li>)}</ul>;
}

/** What the open-source project is, and the facts of the AI-client and command-line paths (moved here from the start page). */
export default async function AboutPage() {
  const { t } = await getMessages();
  const A = t.about;
  return (
    <>
      <h1>{A.title}</h1>
      <p className="lead">{A.lead}</p>

      <h2>{A.pathsTitle}</h2>
      <h3>{A.storedTitle}</h3>
      <List items={A.stored} />
      <h3>{A.notStoredTitle}</h3>
      <List items={A.notStored} testId="about-not-stored" />
      <p className="notice">{A.recordsNote}</p>

      <h2>{A.nonGoalsTitle}</h2>
      <List items={A.nonGoals} />

      <h2>{A.sourceTitle}</h2>
      <ul className="plain">
        <li><a href={A.sourceHref} rel="noopener noreferrer">{A.sourceLink}</a></li>
        <li><Link href="/developers">{A.developersLink}</Link></li>
      </ul>
    </>
  );
}
