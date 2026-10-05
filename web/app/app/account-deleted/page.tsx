import Link from "next/link";
import { DELETION_FACTS } from "@/lib/copy";

export default function AccountDeletedPage() {
  return (
    <>
      <h1>Your account is closed</h1>
      <p data-testid="deleted-removes">{DELETION_FACTS.removes}</p>
      <p data-testid="deleted-not-submissions">{DELETION_FACTS.notSubmissions}</p>
      <p data-testid="deleted-receipt">{DELETION_FACTS.receipt}</p>
      <p><Link href="/">Back to the start</Link></p>
    </>
  );
}
