"use client";

import { useRef, useState } from "react";
import { m } from "@/lib/messages";
import { normaliseReceiptCode, receiptOutcome } from "@/lib/receipt";

const D = m.deleteSubmission;

type State =
  | { kind: "idle" }
  | { kind: "working" }
  | { kind: "uniform" }
  | { kind: "error"; message: string };

/**
 * Deletion by receipt code, with no sign-in. The code is a bearer secret (ADR-0029, ADR-0049): it travels only in the
 * `X-Receipt-Code` header to the BFF's own `/api/receipts`; the input has no `name`, so even a native form submission
 * (JavaScript failed to load) could not put it in a URL, it opts out of autofill and history suggestions, and it is
 * emptied as soon as the request is made. The page never stores it and never shows it back.
 */
export function DeleteSubmission() {
  const [state, setState] = useState<State>({ kind: "idle" });
  const input = useRef<HTMLInputElement>(null);

  async function onSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const code = normaliseReceiptCode(input.current?.value ?? "");
    if (!code) return;
    setState({ kind: "working" });
    if (input.current) input.current.value = "";
    let response: Response;
    try {
      response = await fetch("/api/receipts", { method: "DELETE", headers: { "X-Receipt-Code": code }, cache: "no-store" });
    } catch {
      setState({ kind: "error", message: D.errors.unavailable });
      return;
    }
    const body = response.status === 204 ? null : ((await response.json().catch(() => null)) as unknown);
    const outcome = receiptOutcome(response.status, body, response.headers.get("retry-after"));
    if (outcome.kind === "uniform") return setState({ kind: "uniform" });
    const base = D.errors[outcome.key];
    setState({ kind: "error", message: outcome.retryAfter ? `${base} ${D.waitSeconds} ${outcome.retryAfter} ${D.seconds}.` : base });
  }

  return (
    <>
      <h1>{D.title}</h1>
      <p>{D.lead}</p>

      {state.kind === "uniform" ? (
        <section aria-live="polite">
          <p className="notice ok" role="status" data-testid="receipt-result">
            <strong>{D.uniformAnswer}</strong> {D.uniformDetail}
          </p>
          <button type="button" className="secondary" onClick={() => setState({ kind: "idle" })} data-testid="receipt-again">{D.again}</button>
        </section>
      ) : (
        <form method="post" onSubmit={onSubmit} aria-labelledby="receipt-heading">
          <h2 id="receipt-heading">{D.label}</h2>
          <label htmlFor="receipt-code">{D.label}</label>
          <input
            ref={input}
            id="receipt-code"
            type="text"
            autoComplete="off"
            autoCorrect="off"
            autoCapitalize="off"
            spellCheck={false}
            inputMode="text"
            maxLength={128}
            required
            aria-describedby="receipt-hint"
            data-testid="receipt-input"
          />
          <p id="receipt-hint" className="muted">{D.hint}</p>
          {state.kind === "error" && <p role="alert" data-testid="receipt-error">{state.message}</p>}
          <button type="submit" disabled={state.kind === "working"} data-testid="receipt-submit">
            {state.kind === "working" ? D.working : D.submit}
          </button>
        </form>
      )}

      <p className="muted">{D.lostCode}</p>
      <p className="muted">{D.accountNote}</p>
    </>
  );
}
