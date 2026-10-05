import type { Metadata } from "next";
import { m } from "@/lib/messages";
import { readPage } from "@/lib/signals";
import { SignalsList } from "./SignalsList";

export const metadata: Metadata = { title: m.signals.title };

export default async function SignalsPage({ searchParams }: { searchParams: Promise<{ page?: string | string[] }> }) {
  const { page } = await searchParams;
  return <SignalsList page={readPage(page)} />;
}
