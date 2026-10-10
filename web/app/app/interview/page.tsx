import type { Metadata } from "next";
import { interviewCopy } from "@/lib/messages/interview";
import { Interview } from "./Interview";

export const metadata: Metadata = { title: interviewCopy.en.title };

// The interview is signed-in only: the edge gate (proxy.ts) sends a signed-out visitor to /login and back here. The page itself
// holds no transcript and no result: both live in the client's memory for the length of the visit (ADR-0048).
export default function InterviewPage() {
  return <Interview />;
}
