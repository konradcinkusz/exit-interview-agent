import type { Metadata } from "next";
import { devMessages as m } from "@/lib/messages";
import { EnglishOnly } from "../_components/EnglishOnly";
import { DeleteSubmission } from "./DeleteSubmission";

export const metadata: Metadata = { title: m.deleteSubmission.title };

export default function DeleteSubmissionPage() {
  return (
    <EnglishOnly>
      <DeleteSubmission />
    </EnglishOnly>
  );
}
