import Link from "next/link";
import type { FailureKind, InterviewFailure } from "@/lib/interview-api";
import type { InterviewCopy } from "@/lib/messages/interview";

/** The words for a failure. A rate-limit wait is said in seconds when the service gave one. */
export function failureText(failure: InterviewFailure, copy: InterviewCopy): string {
  const entry = copy.errors[failure.kind];
  return typeof entry === "function" ? entry(failure.retryAfter) : entry;
}

/** Where a failure can be fixed by the person: signing in again, accepting the current terms, or confirming the address. Other failures have no link. */
const FIX: Partial<Record<FailureKind, { href: string; label: (copy: InterviewCopy) => string }>> = {
  unauthenticated: { href: "/login?redirect=%2Finterview", label: (c) => c.signInAgain },
  consent_required: { href: "/consent", label: (c) => c.acceptTerms },
  email_not_verified: { href: "/verify-email", label: (c) => c.confirmEmail },
};

/** One failure, as text and, where there is one, the way out. Always `role="alert"`, so a screen reader announces it. */
export function Notice({ failure, copy }: { failure: InterviewFailure | null; copy: InterviewCopy }) {
  if (!failure) return null;
  const fix = FIX[failure.kind];
  return (
    <p className="notice warn" role="alert">
      {failureText(failure, copy)}
      {fix ? (
        <>
          {" "}
          <Link href={fix.href}>{fix.label(copy)}</Link>
        </>
      ) : null}
    </p>
  );
}
