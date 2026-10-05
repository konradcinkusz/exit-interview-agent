"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { m } from "@/lib/messages";
import type { SignalsFailure } from "@/lib/signals";
import { NothingToShow } from "./views";

const S = m.signals;

/**
 * What a failed read looks like. A 429 says how long the service asked to wait and keeps "Try again" disabled until then:
 * nothing here retries by itself (no timer starts a request; the one timer only enables the button). The component is
 * remounted for each new failure (the page shows "loading" in between), so the wait starts afresh. A 404 is "nothing to show" and never says why.
 */
export function Failure({ failure, signInRedirect, onRetry }: { failure: SignalsFailure; signInRedirect: string; onRetry: () => void }) {
  const router = useRouter();
  const wait = failure.kind === "rate_limited" ? failure.retryAfter : 0;
  const [ready, setReady] = useState(wait === 0);

  useEffect(() => {
    if (failure.kind === "unauthenticated") router.push(`/login?redirect=${encodeURIComponent(signInRedirect)}`);
  }, [failure.kind, router, signInRedirect]);

  useEffect(() => {
    if (wait === 0) return;
    const id = window.setTimeout(() => setReady(true), wait * 1000);
    return () => window.clearTimeout(id);
  }, [wait]);

  if (failure.kind === "not_found") return <NothingToShow />;
  const message = S.errors[failure.kind === "rate_limited" ? "rate_limited" : failure.kind];
  return (
    <section data-testid="signals-failure" data-kind={failure.kind}>
      <p role="alert">{message}</p>
      {failure.kind === "rate_limited" && (
        <p data-testid="signals-wait">{S.waitPrefix} {failure.retryAfter} {S.waitSuffix}</p>
      )}
      {failure.kind !== "unauthenticated" && failure.kind !== "consent_required" && (
        <button type="button" onClick={onRetry} disabled={!ready} data-testid="signals-retry">{S.retry}</button>
      )}
    </section>
  );
}
