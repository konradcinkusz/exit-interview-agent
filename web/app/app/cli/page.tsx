import type { Metadata } from "next";
import { m } from "@/lib/messages";
import { Cli } from "./Cli";

export const metadata: Metadata = { title: m.cli.title };

export default function CliPage() {
  return <Cli />;
}
