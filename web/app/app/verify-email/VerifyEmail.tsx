"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { useSearchParams } from "next/navigation";
import { m } from "@/lib/messages";

const V = m.verifyEmail;
type State = "idle" | "working" | "done" | "failed" | "error";

/**
 * Landing page of the link in the verification email (`?token=&email=`). The token is sent once to the BFF and then removed
 * from the address bar, so it does not stay in the history or in a screenshot (the link is single-use anyway).
 */
export function VerifyEmail() {
  const params = useSearchParams();
  // Read once: removing the token from the address bar below changes the search params, which must not cancel the request.
  const [{ token, email }] = useState(() => ({ token: params.get("token"), email: params.get("email") }));
  const [state, setState] = useState<State>(token && email ? "working" : "idle");
  const [resent, setResent] = useState<null | "ok" | "limited" | "error">(null);

  useEffect(() => {
    if (!token || !email) return;
    let cancelled = false;
    fetch("/api/auth/verify-email", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ email, token }),
    })
      .then((response) => !cancelled && setState(response.ok ? "done" : response.status === 400 ? "failed" : "error"))
      .catch(() => !cancelled && setState("error"));
    window.history.replaceState(null, "", window.location.pathname);
    return () => {
      cancelled = true;
    };
  }, [token, email]);

  async function resend(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setResent(null);
    const response = await fetch("/api/auth/resend-verification", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ email: new FormData(event.currentTarget).get("email") }),
    }).catch(() => null);
    setResent(response?.ok ? "ok" : response?.status === 429 ? "limited" : "error");
  }

  return (
    <>
      <h1>{V.title}</h1>
      {state === "idle" && <p data-testid="verify-status">{V.missing}</p>}
      {state === "working" && <p role="status" data-testid="verify-status">{V.working}</p>}
      {state === "done" && (
        <>
          <p className="notice ok" role="status" data-testid="verify-status">{V.done}</p>
          <p><Link className="button" href="/login">{V.signIn}</Link></p>
        </>
      )}
      {state === "failed" && <p role="alert" data-testid="verify-status">{V.failed}</p>}
      {state === "error" && <p role="alert" data-testid="verify-status">{V.generic}</p>}

      {state !== "done" && (
        <form method="post" onSubmit={resend} aria-labelledby="resend-heading">
          <h2 id="resend-heading">{V.resendTitle}</h2>
          <label htmlFor="resend-email">{V.email}</label>
          <input id="resend-email" name="email" type="email" autoComplete="email" required data-testid="resend-email" />
          <button type="submit" data-testid="resend-submit">{V.resend}</button>
          {resent === "ok" && <p role="status" data-testid="resend-status">{V.resent}</p>}
          {resent === "limited" && <p role="alert" data-testid="resend-status">{V.rateLimited}</p>}
          {resent === "error" && <p role="alert" data-testid="resend-status">{V.generic}</p>}
        </form>
      )}
    </>
  );
}
