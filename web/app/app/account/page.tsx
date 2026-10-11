import type { Metadata } from "next";
import { getMessages } from "@/lib/i18n";
import { Account } from "./Account";

export async function generateMetadata(): Promise<Metadata> {
  return { title: (await getMessages()).t.account.title };
}

export default function Page() {
  return <Account />;
}
