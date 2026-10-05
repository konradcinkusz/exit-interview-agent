import type { Metadata } from "next";
import { m } from "@/lib/messages";
import { SignalsEmployer } from "./SignalsEmployer";

// The title is fixed: an employer reference is not put in the tab title, the history entry's name or anything a browser may sync.
export const metadata: Metadata = { title: m.signals.employerTitle };

export default async function SignalsEmployerPage({ params }: { params: Promise<{ employerRef: string }> }) {
  const { employerRef } = await params;
  return <SignalsEmployer employerRef={employerRef} />;
}
