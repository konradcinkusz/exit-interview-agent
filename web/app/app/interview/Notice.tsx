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
  consent_required: { href: "/consent?redirect=%2Finterview", label: (c) => c.acceptTerms },
  email_not_verified: { href: "/verify-email", label: (c) => c.confirmEmail },
};

/**
 * One failure, as text and, where there is one, the way out as its own line. Always `role="alert"`, so a screen reader announces
 * it. A rate limit with a wait shows the seconds left (`waitLeft`), and says when the wait is over; nothing is retried by itself.
 */
export function Notice({ failure, copy, waitLeft }: { failure: InterviewFailure | null; copy: InterviewCopy; waitLeft?: number }) {
  if (!failure) return null;
  const fix = FIX[failure.kind];
  const waiting = failure.kind === "rate_limited" && failure.retryAfter !== undefined && waitLeft !== undefined;
  // The alert holds only the message, so it is announced once; the seconds tick outside it, silently, and the end of the wait is a
  // polite status.
  return (
    <div className="notice warn" data-testid="interview-notice">
      <p role="alert">
        {failureText(failure, copy)}
      </p>
      {fix ? (
        <p>
          <Link href={fix.href}>{fix.label(copy)}</Link>
        </p>
      ) : null}
      {waiting && waitLeft > 0 ? <p className="hint" data-testid="wait-countdown">{copy.waitCountdown(waitLeft)}</p> : null}
      {waiting && waitLeft === 0 ? <p className="hint" role="status" data-testid="wait-countdown">{copy.waitOver}</p> : null}
    </div>
  );
}
