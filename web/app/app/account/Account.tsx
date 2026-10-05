"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { m } from "@/lib/messages";

const A = m.account;
type ErrorKey = keyof typeof A.errors;

type State = { kind: "loading" } | { kind: "ready"; subject: string } | { kind: "error"; message: string };

/** Protected page: the edge gate has already verified the session; this reads the account through the BFF proxy. */
export function Account() {
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
    setDeleteError(A.errors[(error ?? "") as ErrorKey] ?? A.errors.generic);
    setBusy(false);
  }

  return (
    <>
      <h1>{A.title}</h1>
      {state.kind === "loading" && <p role="status">{m.site.loading}</p>}
      {state.kind === "ready" && <p>{A.signedInAs} <code data-testid="account-subject">{state.subject}</code>.</p>}
      {state.kind === "error" && <p role="alert" data-testid="account-error">{A.loadError} ({state.message}).</p>}
      <button type="button" className="secondary" onClick={logout} data-testid="logout">{A.signOut}</button>

      <h2>{A.exportTitle}</h2>
      <p>{A.exportIntro}</p>
      <p className="notice" data-testid="export-note">{A.exportNot}</p>
      {/* A plain link: the browser downloads the attachment itself, so the data never passes through page JavaScript. */}
      <a className="button secondary" href="/api/auth/export" download data-testid="export-link">{A.exportButton}</a>

      <h2>{A.deleteTitle}</h2>
      <section aria-labelledby="deletion-heading" data-testid="deletion-facts">
        <h3 id="deletion-heading">{A.deletion.headline}</h3>
        <ul>
          <li>{A.deletion.removes}</li>
          <li>{A.deletion.notSubmissions}</li>
          <li>{A.deletion.receipt}</li>
          <li>{A.deletion.ledger}</li>
        </ul>
      </section>
      <form method="post" onSubmit={onDelete} aria-label={A.deleteLabel}>
        <label htmlFor="delete-password">{A.password} <span className="muted">{A.passwordHint}</span></label>
        <input id="delete-password" name="password" type="password" autoComplete="current-password" data-testid="delete-password" />
        <label htmlFor="delete-confirmation">{A.confirm}</label>
        <input id="delete-confirmation" name="confirmation" type="text" autoComplete="off" required data-testid="delete-confirmation" />
        {deleteError && <p role="alert" data-testid="delete-error">{deleteError}</p>}
        <button type="submit" className="danger" disabled={busy} data-testid="delete-submit">{A.deleteSubmit}</button>
      </form>
    </>
  );
}
