import type { Metadata } from "next";
import { m } from "@/lib/messages";
import { Account } from "./Account";

export const metadata: Metadata = { title: m.account.title };

export default function AccountPage() {
  return <Account />;
}
