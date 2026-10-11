"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import type { Messages } from "@/lib/messages";
import { useMessages } from "../_components/I18n";
import { SignOutButton } from "../_components/SignOutButton";

type ErrorKey = keyof Messages["account"]["errors"];

type State = { kind: "loading" } | { kind: "ready"; subject: string } | { kind: "error" };

/**
 * Protected page: the edge gate has already verified the session; this reads the account through the BFF proxy. The access token
 * carries no email address, so the page names the account by its ID, in small print, and never invents a name.
 */
export function Account() {
  const t = useMessages();
  const A = t.account;
  const router = useRouter();
  const [state, setState] = useState<State>({ kind: "loading" });
  const [deleteError, setDeleteError] = useState<ErrorKey | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let cancelled = false;
    fetch("/api/proxy/v1/me")
      .then(async (response) => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        return (await response.json()) as { subject: string };
      })
      .then((me) => !cancelled && setState({ kind: "ready", subject: me.subject }))
      .catch(() => !cancelled && setState({ kind: "error" }));
    return () => {
      cancelled = true;
    };
  }, []);

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
      router.refresh();
      return;
    }
    const { error } = (await response.json().catch(() => ({}))) as { error?: string };
    setDeleteError(error && error in A.errors ? (error as ErrorKey) : "generic");
    setBusy(false);
  }

  return (
    <>
      <h1>{A.title}</h1>
      <section className="card" aria-label={A.signedIn}>
        {state.kind === "loading" && <p role="status">{t.site.loading}</p>}
        {state.kind === "ready" && (
          <>
            <p>{A.signedIn}</p>
            <p className="hint">
              {A.accountId}: <code data-testid="account-subject">{state.subject}</code>
            </p>
          </>
        )}
        {state.kind === "error" && <p role="alert" data-testid="account-error">{A.loadError}</p>}
        <SignOutButton label={A.signOut} className="secondary" testId="logout" />
      </section>

      <section className="card" aria-labelledby="account-interview-heading">
        <h2 id="account-interview-heading">{A.interviewTitle}</h2>
        <p>{A.interviewText}</p>
        <Link className="button primary" href="/interview" prefetch={false}>{A.interviewLink}</Link>
      </section>

      <section className="card" aria-labelledby="export-heading">
        <h2 id="export-heading">{A.exportTitle}</h2>
        <p>{A.exportIntro}</p>
        <p className="notice" data-testid="export-note">{A.exportNot}</p>
        {/* A plain link: the browser downloads the attachment itself, so the data never passes through page JavaScript. */}
        <a className="button secondary" href="/api/auth/export" download data-testid="export-link">{A.exportButton}</a>
      </section>

      <section className="card danger-zone" aria-labelledby="delete-heading">
        <h2 id="delete-heading">{A.deleteTitle}</h2>
        <div data-testid="deletion-facts">
          <h3 id="deletion-heading">{A.deletion.headline}</h3>
          <ul>
            <li>{A.deletion.removes}</li>
            <li>{A.deletion.notSubmissions}</li>
            <li>{A.deletion.receipt}</li>
            <li>{A.deletion.ledger}</li>
          </ul>
        </div>
        <form method="post" onSubmit={onDelete} aria-label={A.deleteLabel}>
          <label htmlFor="delete-password">{A.password} <span className="hint-inline">{A.passwordHint}</span></label>
          <input id="delete-password" name="password" type="password" autoComplete="current-password" data-testid="delete-password" />
          <label htmlFor="delete-confirmation">{A.confirm}</label>
          <input id="delete-confirmation" name="confirmation" type="text" autoComplete="off" required data-testid="delete-confirmation" />
          {deleteError && <p role="alert" data-testid="delete-error">{A.errors[deleteError]}</p>}
          <button type="submit" className="danger" disabled={busy} data-testid="delete-submit">{A.deleteSubmit}</button>
        </form>
      </section>
    </>
  );
}
