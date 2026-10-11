import type { Metadata } from "next";
import { devMessages as m } from "@/lib/messages";
import { EnglishOnly } from "../_components/EnglishOnly";
import { readPage } from "@/lib/signals";
import { SignalsList } from "./SignalsList";

export const metadata: Metadata = { title: m.signals.title };

export default async function SignalsPage({ searchParams }: { searchParams: Promise<{ page?: string | string[] }> }) {
  const { page } = await searchParams;
  return (
    <EnglishOnly>
      <SignalsList page={readPage(page)} />
    </EnglishOnly>
  );
}
