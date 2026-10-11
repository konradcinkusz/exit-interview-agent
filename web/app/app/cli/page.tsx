import type { Metadata } from "next";
import { devMessages as m } from "@/lib/messages";
import { EnglishOnly } from "../_components/EnglishOnly";
import { Cli } from "./Cli";

export const metadata: Metadata = { title: m.cli.title };

export default function CliPage() {
  return (
    <EnglishOnly>
      <Cli />
    </EnglishOnly>
  );
}
