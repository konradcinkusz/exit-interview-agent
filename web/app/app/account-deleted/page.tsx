import type { Metadata } from "next";
import Link from "next/link";
import { getMessages } from "@/lib/i18n";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getMessages()).t.accountDeleted.title };
}

export default async function AccountDeletedPage() {
  const { t } = await getMessages();
  const A = t.accountDeleted;
  const D = t.account.deletion;
  return (
    <>
      <h1>{A.title}</h1>
      <p data-testid="deleted-removes">{D.removes}</p>
      <p data-testid="deleted-not-submissions">{D.notSubmissions}</p>
      <p data-testid="deleted-receipt">{D.receipt}</p>
      <div className="actions">
        <Link className="button primary" href="/">{A.back}</Link>
        <Link className="button secondary" href="/delete-submission">{A.deleteSubmission}</Link>
      </div>
    </>
  );
}
