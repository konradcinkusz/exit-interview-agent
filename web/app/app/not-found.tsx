import type { Metadata } from "next";
import Link from "next/link";
import { m } from "@/lib/messages";

export const metadata: Metadata = { title: m.errors.notFoundTitle };

export default function NotFound() {
  return (
    <>
      <h1>{m.errors.notFoundTitle}</h1>
      <p data-testid="not-found">{m.errors.notFound}</p>
      <p><Link href="/">{m.errors.home}</Link></p>
    </>
  );
}
