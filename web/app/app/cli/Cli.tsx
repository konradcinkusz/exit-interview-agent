"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { m } from "@/lib/messages";
import { formatRemaining, parseTicket, ticketErrorKey, type Minted } from "@/lib/ticket";
import { CopyButton } from "../_components/CopyButton";

const C = m.cli;

type State =
  | { kind: "idle" }
  | { kind: "creating" }
  | { kind: "shown"; minted: Minted }
  | { kind: "gone"; reason: "expired" | "cleared" }
  | { kind: "error"; message: string };

/**
 * The ticket lives in this component's memory and nowhere else: not in storage, a cookie, a URL, a log or an analytics
 * call (ADR-0048). It is cleared by expiry, by the button, by leaving the page (unmount), by `pagehide` and when the
 * page is restored from the back/forward cache. A reload starts from `idle`, so it cannot show the ticket again.
 */
export function Cli() {
  const router = useRouter();
  const [state, setState] = useState<State>({ kind: "idle" });
  const [now, setNow] = useState(() => Date.now());
  const heading = useRef<HTMLHeadingElement>(null);

  const clear = useCallback((reason: "expired" | "cleared") => setState({ kind: "gone", reason }), []);

  useEffect(() => {
    if (state.kind !== "shown") return;
    const expiresAt = state.minted.expiresAt;
    const tick = () => {
      const t = Date.now();
      setNow(t);
      if (t >= expiresAt) clear("expired");
    };
    tick();
    const id = window.setInterval(tick, 1000);
    return () => window.clearInterval(id);
  }, [state, clear]);

  useEffect(() => {
    const drop = () => setState((s) => (s.kind === "shown" ? { kind: "gone", reason: "cleared" } : s));
    const onShow = (e: PageTransitionEvent) => e.persisted && drop();
    window.addEventListener("pagehide", drop);
    window.addEventListener("pageshow", onShow);
    return () => {
      window.removeEventListener("pagehide", drop);
      window.removeEventListener("pageshow", onShow);
    };
  }, []);

  useEffect(() => {
    if (state.kind === "shown") heading.current?.focus();
  }, [state.kind]);

  async function create() {
    setState({ kind: "creating" });
    let response: Response;
    try {
      response = await fetch("/api/proxy/v1/tickets", { method: "POST", cache: "no-store" });
    } catch {
      setState({ kind: "error", message: C.errors.unavailable });
      return;
    }
    const body = (await response.json().catch(() => null)) as unknown;
    if (response.status === 201) {
      const minted = parseTicket(body);
      if (minted) return setState({ kind: "shown", minted });
      return setState({ kind: "error", message: C.errors.generic });
    }
    if (response.status === 401) {
      router.push(`/login?redirect=${encodeURIComponent("/cli")}`);
      return;
    }
    const { key, retryAfter } = ticketErrorKey(response.status, body);
    const base = C.errors[key];
    setState({ kind: "error", message: retryAfter ? `${base} ${C.waitSeconds} ${retryAfter} ${C.seconds}.` : base });
  }

  return (
    <>
      <h1>{C.title}</h1>
      <p>{C.lead}</p>

      <h2>{C.ticketTitle}</h2>
      <ul className="plain" data-testid="ticket-facts">{C.ticketFacts.map((f) => <li key={f}>{f}</li>)}</ul>
      <p className="notice">{C.shownOnce}</p>

      {state.kind !== "shown" && (
        <button type="button" onClick={create} disabled={state.kind === "creating"} data-testid="ticket-create">
          {state.kind === "creating" ? C.creating : C.create}
        </button>
      )}
      {state.kind === "gone" && (
        <p role="status" data-testid="ticket-gone">{state.reason === "expired" ? C.expired : C.cleared}</p>
      )}
      {state.kind === "error" && <p role="alert" data-testid="ticket-error">{state.message}</p>}

      {state.kind === "shown" && (
        <section aria-labelledby="ticket-heading" data-testid="ticket-panel">
          <h3 id="ticket-heading" ref={heading} tabIndex={-1}>{C.yourTicket}</h3>
          <code className="secret" data-testid="ticket-value">{state.minted.ticket}</code>
          <p>
            {C.expiresIn} <strong data-testid="ticket-countdown">{formatRemaining(state.minted.expiresAt - now)}</strong>
          </p>
          <CopyButton value={state.minted.ticket} label={C.copy} done={C.copied} failed={C.copyFailed} testId="ticket-copy" />
          <button type="button" className="secondary" onClick={() => clear("cleared")} data-testid="ticket-clear">{C.clear}</button>
        </section>
      )}

      <h2>{C.commandsTitle}</h2>
      <p>{C.commandsIntro}</p>
      <dl className="commands" data-testid="cli-commands">
        {C.commands.map((c) => (
          <div key={c.command}>
            <dt><code>{c.command}</code></dt>
            <dd>{c.what}</dd>
          </div>
        ))}
      </dl>
      <p className="notice" data-testid="cli-submit-note">{C.commandsNote}</p>
    </>
  );
}
