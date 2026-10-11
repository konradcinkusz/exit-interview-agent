import type { Metadata } from "next";
import { getLocale } from "@/lib/i18n";
import { interviewCopy } from "@/lib/messages/interview";
import { devHints } from "@/lib/runtime-config";
import { Interview } from "./Interview";

export async function generateMetadata(): Promise<Metadata> {
  return { title: interviewCopy[await getLocale()].title };
}

// The interview is signed-in only: the edge gate (proxy.ts) sends a signed-out visitor to /login and back here. The page itself
// holds no transcript and no result: both live in the client's memory for the length of the visit (ADR-0048). `devHints` is true
// only in the local AppHost stack (DEV_HINTS=1), where a no-credit start screen says how to get the test account's credits.
export default function InterviewPage() {
  return <Interview devHints={devHints() !== null} />;
}
