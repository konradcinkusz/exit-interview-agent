import type { Metadata } from "next";
import { m } from "@/lib/messages";
import { DeleteSubmission } from "./DeleteSubmission";

export const metadata: Metadata = { title: m.deleteSubmission.title };

export default function DeleteSubmissionPage() {
  return <DeleteSubmission />;
}
