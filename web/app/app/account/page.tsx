"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { DELETION_FACTS } from "@/lib/copy";

type State = { kind: "loading" } | { kind: "ready"; subject: string } | { kind: "error"; message: string };

const DELETE_MESSAGES: Record<string, string> = {
  password_required: "Enter your password to confirm.",
  invalid_password: "That password is not correct.",
  confirmation_required: "Type DELETE exactly to confirm.",
  unauthenticated: "Your session has ended. Sign in again.",
  identity_unavailable: "The identity service could not be reached. Nothing was deleted; try again.",
};

/** Protected page: the edge gate has already verified the session; this reads the account through the BFF proxy. */
export default function AccountPage() {
  const router = useRouter();
  const [state, setState] = useState<State>({ kind: "loading" });
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let cancelled = false;
    fetch("/api/proxy/v1/me")
      .then(async (response) => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        return (await response.json()) as { subject: string };
      })
      .then((me) => !cancelled && setState({ kind: "ready", subject: me.subject }))
      .catch((e: Error) => !cancelled && setState({ kind: "error", message: e.message }));
    return () => {
      cancelled = true;
    };
  }, []);

  async function logout() {
    await fetch("/api/auth/session", { method: "DELETE" });
    router.push("/");
  }

  async function onDelete(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    setDeleteError(null);
    const form = new FormData(event.currentTarget);
    const response = await fetch("/api/auth/account", {
      method: "DELETE",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ password: form.get("password") || undefined, confirmation: form.get("confirmation") }),
    });
    if (response.ok) {
      router.push("/account-deleted");
      return;
    }
    const { error } = (await response.json().catch(() => ({}))) as { error?: string };
    setDeleteError(DELETE_MESSAGES[error ?? ""] ?? "Could not delete the account. Try again.");
    setBusy(false);
  }

  return (
    <>
      <h1>My account</h1>
      {state.kind === "loading" && <p>Loading…</p>}
      {state.kind === "ready" && (
        <p>Signed in as account <code data-testid="account-subject">{state.subject}</code>.</p>
      )}
      {state.kind === "error" && <p role="alert" data-testid="account-error">Could not load your account ({state.message}).</p>}
      <button onClick={logout} data-testid="logout">Sign out</button>

      <h2>Delete account</h2>
      <section aria-labelledby="deletion-heading" data-testid="deletion-facts">
        <h3 id="deletion-heading">{DELETION_FACTS.headline}</h3>
        <ul>
          <li>{DELETION_FACTS.removes}</li>
          <li>{DELETION_FACTS.notSubmissions}</li>
          <li>{DELETION_FACTS.receipt}</li>
          <li>{DELETION_FACTS.ledger}</li>
        </ul>
      </section>
      <form onSubmit={onDelete} aria-label="Delete account">
        <label htmlFor="delete-password">Password <span className="muted">(not needed if you sign in with an external provider)</span></label>
        <input id="delete-password" name="password" type="password" autoComplete="current-password" data-testid="delete-password" />
        <label htmlFor="delete-confirmation">Type DELETE to confirm</label>
        <input id="delete-confirmation" name="confirmation" type="text" autoComplete="off" required data-testid="delete-confirmation" />
        {deleteError && <p role="alert" data-testid="delete-error">{deleteError}</p>}
        <button type="submit" disabled={busy} data-testid="delete-submit">Delete my account</button>
      </form>
    </>
  );
}
