"use client";

import { Suspense, useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { CONSENT_SUMMARY } from "@/lib/copy";
import { safeRedirect } from "@/lib/safe-redirect";

type State =
  | { kind: "loading" }
  | { kind: "ready"; terms: string; privacy: string }
  | { kind: "error"; message: string };

/**
 * The consent step: shown right after sign-in, and again whenever authservice's Terms or Privacy version moves,
 * before anything else in the app. Acceptance is recorded by authservice (immutable row: version, time, locale);
 * this page only collects the "yes" and never sees or stores a token.
 */
function ConsentForm() {
  const router = useRouter();
  const redirect = safeRedirect(useSearchParams().get("redirect"));
  const [state, setState] = useState<State>({ kind: "loading" });
  const [terms, setTerms] = useState(false);
  const [privacy, setPrivacy] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let cancelled = false;
    fetch("/api/auth/consent")
      .then(async (response) => {
        if (response.status === 401) return router.replace(`/login?redirect=${encodeURIComponent(redirect)}`);
        if (!response.ok) throw new Error("unavailable");
        const status = (await response.json()) as { required: boolean; terms: string; privacy: string };
        if (!status.required) return router.replace(redirect); // nothing to accept (already done in another tab)
        if (!cancelled) setState({ kind: "ready", terms: status.terms, privacy: status.privacy });
      })
      .catch(() => !cancelled && setState({ kind: "error", message: "Could not check your consent status. Try again." }));
    return () => {
      cancelled = true;
    };
  }, [redirect, router]);

  async function accept(event: React.FormEvent) {
    event.preventDefault();
    setBusy(true);
    const response = await fetch("/api/auth/consent", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ acceptTerms: terms, acceptPrivacy: privacy }),
    });
    if (response.ok) return router.replace(redirect);
    setState({ kind: "error", message: "Could not record your consent. Try again." });
    setBusy(false);
  }

  async function decline() {
    await fetch("/api/auth/session", { method: "DELETE" });
    router.replace("/");
  }

  return (
    <>
      <h1>Before you continue</h1>
      {state.kind === "loading" && <p>Loading…</p>}
      {state.kind === "error" && <p role="alert" data-testid="consent-error">{state.message}</p>}
      {state.kind === "ready" && (
        <form onSubmit={accept} aria-labelledby="consent-heading">
          <p id="consent-heading">
            To use this service you need to accept the current versions of these documents. If either has changed since
            you last accepted, you are asked again.
          </p>
          <label>
            <input type="checkbox" checked={terms} onChange={(e) => setTerms(e.target.checked)} data-testid="consent-terms" style={{ width: "auto" }} />{" "}
            I accept the Terms of Use, version <code data-testid="consent-terms-version">{state.terms}</code>. <span className="muted">{CONSENT_SUMMARY.terms}</span>
          </label>
          <label>
            <input type="checkbox" checked={privacy} onChange={(e) => setPrivacy(e.target.checked)} data-testid="consent-privacy" style={{ width: "auto" }} />{" "}
            I accept the Privacy Policy, version <code data-testid="consent-privacy-version">{state.privacy}</code>. <span className="muted">{CONSENT_SUMMARY.privacy}</span>
          </label>
          <button type="submit" disabled={busy || !terms || !privacy} data-testid="consent-accept">Accept and continue</button>{" "}
          <button type="button" onClick={decline} data-testid="consent-decline">Decline and sign out</button>
        </form>
      )}
    </>
  );
}

export default function ConsentPage() {
  return (
    <Suspense fallback={<p>Loading…</p>}>
      <ConsentForm />
    </Suspense>
  );
}
