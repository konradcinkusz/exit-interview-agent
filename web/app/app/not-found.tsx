import type { Metadata } from "next";
import Link from "next/link";
import { getMessages } from "@/lib/i18n";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getMessages()).t.errors.notFoundTitle };
}

export default async function NotFound() {
  const { t } = await getMessages();
  return (
    <>
      <h1>{t.errors.notFoundTitle}</h1>
      <p data-testid="not-found">{t.errors.notFound}</p>
      <p><Link className="button primary" href="/">{t.errors.home}</Link></p>
    </>
  );
}
