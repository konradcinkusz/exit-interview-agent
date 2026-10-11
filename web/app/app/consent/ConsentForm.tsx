"use client";

import { useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { safeRedirect } from "@/lib/safe-redirect";
import { useMessages } from "../_components/I18n";

type State =
  | { kind: "loading" }
  | { kind: "ready"; terms: string; privacy: string }
  | { kind: "error"; message: "load" | "save" };

/**
 * The consent step: shown right after sign-in, and again whenever authservice's Terms or Privacy version moves,
 * before anything else in the app. Acceptance is recorded by authservice (immutable row: version, time, locale);
 * this page only collects the "yes" and never sees or stores a token.
 */
export function ConsentForm() {
  const t = useMessages();
  const C = t.consent;
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
      .catch(() => !cancelled && setState({ kind: "error", message: "load" }));
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
    // A full page load, so the header is rendered again for an account that may now use the whole site.
    if (response.ok) return window.location.replace(redirect);
    setState({ kind: "error", message: "save" });
    setBusy(false);
  }

  async function decline() {
    await fetch("/api/auth/session", { method: "DELETE" });
    window.location.replace("/");
  }

  return (
    <>
      <h1>{C.title}</h1>
      {state.kind === "loading" && <p role="status">{t.site.loading}</p>}
      {state.kind === "error" && <p role="alert" data-testid="consent-error">{state.message === "load" ? C.loadError : C.saveError}</p>}
      {state.kind === "ready" && (
        <form onSubmit={accept} aria-labelledby="consent-heading">
          <p id="consent-heading" className="lead">{C.intro}</p>
          <label className="check">
            <input type="checkbox" checked={terms} onChange={(e) => setTerms(e.target.checked)} aria-describedby="consent-terms-detail" data-testid="consent-terms" />
            <span>
              {C.terms}
              <span id="consent-terms-detail" className="hint">
                {C.termsSummary} {C.version} <span data-testid="consent-terms-version">{state.terms}</span>
              </span>
            </span>
          </label>
          <label className="check">
            <input type="checkbox" checked={privacy} onChange={(e) => setPrivacy(e.target.checked)} aria-describedby="consent-privacy-detail" data-testid="consent-privacy" />
            <span>
              {C.privacy}
              <span id="consent-privacy-detail" className="hint">
                {C.privacySummary} {C.version} <span data-testid="consent-privacy-version">{state.privacy}</span>
              </span>
            </span>
          </label>
          <div className="actions">
            <button type="submit" className="primary" disabled={busy || !terms || !privacy} data-testid="consent-accept">{C.accept}</button>
            <button type="button" className="secondary" onClick={decline} data-testid="consent-decline">{C.decline}</button>
          </div>
        </form>
      )}
    </>
  );
}
