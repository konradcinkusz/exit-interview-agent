"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { m } from "@/lib/messages";
import { CopyButton } from "../_components/CopyButton";

const C = m.connect;
type State = { kind: "loading" } | { kind: "ready"; url: string | null } | { kind: "error" };

/** The connector address comes from the runtime config route, so one image serves every environment (FRONTEND-BFF §2). */
export function Connect() {
  const [state, setState] = useState<State>({ kind: "loading" });

  useEffect(() => {
    let cancelled = false;
    fetch("/api/config")
      .then(async (response) => {
        if (!response.ok) throw new Error(String(response.status));
        return (await response.json()) as { mcp?: { url?: string | null } };
      })
      .then((config) => !cancelled && setState({ kind: "ready", url: config.mcp?.url ?? null }))
      .catch(() => !cancelled && setState({ kind: "error" }));
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <>
      <h1>{C.title}</h1>
      <p>{C.lead}</p>

      <h2>{C.urlTitle}</h2>
      {state.kind === "loading" && <p role="status">{C.config.loading}</p>}
      {state.kind === "error" && <p role="alert" data-testid="connect-error">{C.config.error}</p>}
      {state.kind === "ready" && state.url && (
        <>
          <p>{C.urlIntro}</p>
          <code className="secret" data-testid="mcp-url">{state.url}</code>
          <CopyButton value={state.url} label={C.copy} done={C.copied} failed={C.copyFailed} testId="mcp-copy" />
        </>
      )}
      {state.kind === "ready" && !state.url && <p className="notice" data-testid="mcp-missing">{C.urlMissing}</p>}

      <h2>{C.stepsTitle}</h2>
      <ol>{C.steps.map((s) => <li key={s}>{s}</li>)}</ol>
      <p className="muted">{C.plansNote}</p>

      <h2>{C.canTitle}</h2>
      <ul className="plain">{C.can.map((s) => <li key={s}>{s}</li>)}</ul>
      <h2>{C.cannotTitle}</h2>
      <ul className="plain" data-testid="connect-cannot">{C.cannot.map((s) => <li key={s}>{s}</li>)}</ul>
      <p className="notice">{C.privacyNote}</p>
      <p className="muted" data-testid="connect-runbook">
        {C.runbookNote}{" "}
        <a href={C.runbookHref} rel="noreferrer noopener">{C.runbookLabel}</a>{" "}
        (<code>{C.runbookPath}</code>)
      </p>
      <p><Link href="/privacy">{C.privacyLink}</Link> · <Link href="/delete-submission">{C.deleteLink}</Link></p>
    </>
  );
}
