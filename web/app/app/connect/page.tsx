import type { Metadata } from "next";
import { devMessages as m } from "@/lib/messages";
import { EnglishOnly } from "../_components/EnglishOnly";
import { Connect } from "./Connect";

export const metadata: Metadata = { title: m.connect.title };

export default function ConnectPage() {
  return (
    <EnglishOnly>
      <Connect />
    </EnglishOnly>
  );
}
