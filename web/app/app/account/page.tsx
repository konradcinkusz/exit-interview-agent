"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";

type State = { kind: "loading" } | { kind: "ready"; subject: string } | { kind: "error"; message: string };

/** Protected page: the edge gate has already verified the session; this reads the account through the BFF proxy. */
export default function AccountPage() {
  const router = useRouter();
  const [state, setState] = useState<State>({ kind: "loading" });

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

  return (
    <>
      <h1>My account</h1>
      {state.kind === "loading" && <p>Loading…</p>}
      {state.kind === "ready" && (
        <p>Signed in as account <code data-testid="account-subject">{state.subject}</code>.</p>
      )}
      {state.kind === "error" && <p role="alert" data-testid="account-error">Could not load your account ({state.message}).</p>}
      <button onClick={logout} data-testid="logout">Sign out</button>
    </>
  );
}
