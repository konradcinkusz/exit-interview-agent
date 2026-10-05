"use client";

import { Suspense, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { safeRedirect } from "@/lib/safe-redirect";

const MESSAGES: Record<string, string> = {
  invalid_credentials: "Invalid email or password.",
  email_not_verified: "Verify your email address first.",
  rate_limited: "Too many attempts. Try again in a minute.",
  identity_not_configured: "Sign-in is not configured in this environment.",
  two_factor_not_supported: "Two-factor sign-in is not supported here yet.",
};

function LoginForm() {
  const router = useRouter();
  const redirect = safeRedirect(useSearchParams().get("redirect"));
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    const form = new FormData(event.currentTarget);
    const response = await fetch("/api/auth/login", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ email: form.get("email"), password: form.get("password") }),
    });
    if (response.ok) {
      // Consent before anything else: an account that has not accepted the versions in force goes to that step first.
      const { consentRequired } = (await response.json().catch(() => ({}))) as { consentRequired?: boolean };
      router.push(consentRequired ? `/consent?redirect=${encodeURIComponent(redirect)}` : redirect);
      return;
    }
    const { error: code } = (await response.json().catch(() => ({}))) as { error?: string };
    setError(MESSAGES[code ?? ""] ?? "Sign-in failed. Try again.");
    setBusy(false);
  }

  return (
    <form onSubmit={onSubmit} aria-labelledby="login-heading">
      <h1 id="login-heading">Sign in</h1>
      <label htmlFor="email">Email</label>
      <input id="email" name="email" type="email" autoComplete="username" required data-testid="login-email" />
      <label htmlFor="password">Password</label>
      <input id="password" name="password" type="password" autoComplete="current-password" required data-testid="login-password" />
      {error && <p role="alert" data-testid="login-error">{error}</p>}
      <button type="submit" disabled={busy} data-testid="login-submit">Sign in</button>
    </form>
  );
}

export default function LoginPage() {
  return (
    <Suspense fallback={<p>Loading…</p>}>
      <LoginForm />
    </Suspense>
  );
}
