import type { Metadata } from "next";
import Link from "next/link";
import { m } from "@/lib/messages";

const A = m.accountDeleted;
const D = m.account.deletion;
export const metadata: Metadata = { title: A.title };

export default function AccountDeletedPage() {
  return (
    <>
      <h1>{A.title}</h1>
      <p data-testid="deleted-removes">{D.removes}</p>
      <p data-testid="deleted-not-submissions">{D.notSubmissions}</p>
      <p data-testid="deleted-receipt">{D.receipt}</p>
      <p><Link href="/delete-submission">{A.deleteSubmission}</Link></p>
      <p><Link href="/">{A.back}</Link></p>
    </>
  );
}
