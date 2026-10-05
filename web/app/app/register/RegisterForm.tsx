"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { m } from "@/lib/messages";

const R = m.register;
type ErrorKey = keyof typeof R.errors;

type Versions = { terms: string; privacy: string } | null;

export function RegisterForm() {
  const [versions, setVersions] = useState<Versions>(null);
  const [versionsError, setVersionsError] = useState(false);
  const [terms, setTerms] = useState(false);
  const [privacy, setPrivacy] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [details, setDetails] = useState<string[]>([]);
  const [done, setDone] = useState<null | { verify: boolean }>(null);

  useEffect(() => {
    let cancelled = false;
    fetch("/api/auth/consent-versions")
      .then(async (response) => {
        if (!response.ok) throw new Error(String(response.status));
        return (await response.json()) as { terms: string; privacy: string };
      })
      .then((v) => !cancelled && setVersions(v))
      .catch(() => !cancelled && setVersionsError(true));
    return () => {
      cancelled = true;
    };
  }, []);

  async function onSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    setDetails([]);
    const form = new FormData(event.currentTarget);
    const response = await fetch("/api/auth/register", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ email: form.get("email"), password: form.get("password"), acceptTerms: terms, acceptPrivacy: privacy }),
    });
    const body = (await response.json().catch(() => ({}))) as { error?: string; messages?: string[]; verificationRequired?: boolean };
    if (response.ok) {
      setDone({ verify: Boolean(body.verificationRequired) });
      return;
    }
    setError(R.errors[(body.error ?? "") as ErrorKey] ?? R.errors.generic);
    setDetails(Array.isArray(body.messages) ? body.messages : []);
    setBusy(false);
  }

  if (done) {
    return (
      <>
        <h1>{R.title}</h1>
        <p className="notice ok" role="status" data-testid="register-done">{done.verify ? R.createdVerify : R.created}</p>
        <p><Link className="button" href="/login">{m.login.submit}</Link></p>
      </>
    );
  }

  return (
    <form method="post" onSubmit={onSubmit} aria-labelledby="register-heading">
      <h1 id="register-heading">{R.title}</h1>
      <p>{R.intro}</p>
      <label htmlFor="email">{R.email}</label>
      <input id="email" name="email" type="email" autoComplete="email" required data-testid="register-email" />
      <label htmlFor="password">{R.password}</label>
      <input id="password" name="password" type="password" autoComplete="new-password" minLength={8} maxLength={100} required aria-describedby="password-hint" data-testid="register-password" />
      <p id="password-hint" className="muted">{R.passwordHint}</p>

      {versionsError && <p role="alert" data-testid="register-error">{R.errors.generic}</p>}
      {versions && (
        <>
          <label className="check">
            <input type="checkbox" checked={terms} onChange={(e) => setTerms(e.target.checked)} data-testid="register-terms" />
            {R.termsPrefix} <code>{versions.terms}</code>
          </label>
          <label className="check">
            <input type="checkbox" checked={privacy} onChange={(e) => setPrivacy(e.target.checked)} data-testid="register-privacy" />
            {R.privacyPrefix} <code>{versions.privacy}</code>
          </label>
        </>
      )}
      {error && <p role="alert" data-testid="register-error">{error}</p>}
      {details.length > 0 && <ul>{details.map((d) => <li key={d}>{d}</li>)}</ul>}
      <button type="submit" disabled={busy || !versions || !terms || !privacy} data-testid="register-submit">{R.submit}</button>
      <p><Link href="/login">{R.signIn}</Link></p>
    </form>
  );
}
