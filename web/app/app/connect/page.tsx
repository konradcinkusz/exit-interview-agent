import type { Metadata } from "next";
import { m } from "@/lib/messages";
import { Connect } from "./Connect";

export const metadata: Metadata = { title: m.connect.title };

export default function ConnectPage() {
  return <Connect />;
}
