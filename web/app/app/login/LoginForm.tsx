"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { m } from "@/lib/messages";
import { safeRedirect } from "@/lib/safe-redirect";

const L = m.login;
type ErrorKey = keyof typeof L.errors;
const message = (code: string | undefined): string => L.errors[(code ?? "") as ErrorKey] ?? L.errors.generic;

type Step = "password" | "code" | "recovery";

export function LoginForm() {
  const router = useRouter();
  const redirect = safeRedirect(useSearchParams().get("redirect"));
  const [step, setStep] = useState<Step>("password");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const codeInput = useRef<HTMLInputElement>(null);
  const emailInput = useRef<HTMLInputElement>(null);

  // Focus follows the step: the code field when it appears, the email field when the user is sent back. Not on first load:
  // the first Tab stop on a page is the skip link.
  const changedStep = useRef(false);
  useEffect(() => {
    if (!changedStep.current) {
      changedStep.current = true;
      return;
    }
    if (step === "password") emailInput.current?.focus();
    else codeInput.current?.focus();
  }, [step]);

  function finish(consentRequired: boolean | undefined) {
    // Consent before anything else: an account that has not accepted the versions in force goes to that step first.
    router.push(consentRequired ? `/consent?redirect=${encodeURIComponent(redirect)}` : redirect);
  }

  async function onPassword(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    const form = new FormData(event.currentTarget);
    const response = await fetch("/api/auth/login", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ email: form.get("email"), password: form.get("password") }),
    });
    const body = (await response.json().catch(() => ({}))) as { error?: string; consentRequired?: boolean; twoFactorRequired?: boolean };
    if (response.ok && body.twoFactorRequired) {
      // The password form unmounts with the step, which discards the password; the challenge is in an HttpOnly cookie.
      setStep("code");
      setBusy(false);
      return;
    }
    if (response.ok) return finish(body.consentRequired);
    setError(message(body.error));
    setBusy(false);
  }

  async function onSecondFactor(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    const value = String(new FormData(event.currentTarget).get("factor") ?? "");
    const response = await fetch("/api/auth/two-factor", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(step === "recovery" ? { recoveryCode: value } : { code: value }),
    });
    const body = (await response.json().catch(() => ({}))) as { error?: string; consentRequired?: boolean };
    if (response.ok) return finish(body.consentRequired);
    if (body.error === "challenge_expired" || body.error === "locked") setStep("password");
    setError(message(body.error));
    setBusy(false);
  }

  if (step === "password") {
    return (
      <form method="post" onSubmit={onPassword} aria-labelledby="login-heading">
        <h1 id="login-heading">{L.title}</h1>
        <label htmlFor="email">{L.email}</label>
        <input ref={emailInput} id="email" name="email" type="email" autoComplete="username" required data-testid="login-email" />
        <label htmlFor="password">{L.password}</label>
        <input id="password" name="password" type="password" autoComplete="current-password" required data-testid="login-password" />
        {error && <p role="alert" data-testid="login-error">{error}</p>}
        <button type="submit" disabled={busy} data-testid="login-submit">{L.submit}</button>
        <p><Link href="/register">{L.register}</Link></p>
      </form>
    );
  }

  const recovery = step === "recovery";
  return (
    <form method="post" onSubmit={onSecondFactor} aria-labelledby="twofactor-heading" data-testid="twofactor-form">
      <h1 id="twofactor-heading">{L.twoFactorTitle}</h1>
      <p>{L.twoFactorIntro}</p>
      <label htmlFor="factor">{recovery ? L.recoveryCode : L.code}</label>
      <input
        ref={codeInput}
        key={step}
        id="factor"
        name="factor"
        type="text"
        // One-time codes are numeric and must not be remembered or offered back by the browser.
        inputMode={recovery ? "text" : "numeric"}
        autoComplete={recovery ? "off" : "one-time-code"}
        autoCapitalize="off"
        spellCheck={false}
        required
        aria-describedby={recovery ? "recovery-hint" : undefined}
        data-testid="twofactor-code"
      />
      {recovery && <p id="recovery-hint" className="muted">{L.recoveryHint}</p>}
      {error && <p role="alert" data-testid="login-error">{error}</p>}
      <button type="submit" disabled={busy} data-testid="twofactor-submit">{L.twoFactorSubmit}</button>
      <button type="button" className="secondary" onClick={() => { setError(null); setStep(recovery ? "code" : "recovery"); }} data-testid="twofactor-toggle">
        {recovery ? L.useCode : L.useRecovery}
      </button>
      <button type="button" className="secondary" onClick={() => { setError(null); setStep("password"); }}>{L.twoFactorBack}</button>
    </form>
  );
}
