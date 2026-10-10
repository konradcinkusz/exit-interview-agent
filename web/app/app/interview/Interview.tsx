"use client";

import { useCallback, useEffect, useReducer, useRef } from "react";
import { interviewApi, safeCheckoutUrl } from "@/lib/interview-api";
import type { InterviewStatus } from "@/lib/interview-contract";
import { interviewCopy, type InterviewCopy } from "@/lib/messages/interview";
import { ChatPanel } from "./ChatPanel";
import { ConsentPanel } from "./ConsentPanel";
import { EndedPanel } from "./EndedPanel";
import { ResultPanel } from "./ResultPanel";
import { StartPanel } from "./StartPanel";
import { initialState, reduce } from "./state";

const SETTLE_ATTEMPTS = 40;
const SETTLE_DELAY_MS = 500;

// The container: one reducer, the API client, and the effects that must run once per step. Each panel receives the copy of the
// interview's language and dispatches; none of them holds a secret, a token or a URL with interview content (ADR-0048).

export function Interview() {
  const [state, dispatch] = useReducer(reduce, undefined, initialState);
  const copy: InterviewCopy = interviewCopy[state.language];
  const resultRequested = useRef(false);

  // The page's language follows the browser on the first visit, and then the person's choice. The layout fixes `lang="en"`; this
  // keeps the document's language true for the pages that are Polish.
  useEffect(() => {
    dispatch({ type: "language", language: navigator.language.toLowerCase().startsWith("pl") ? "pl" : "en" });
  }, []);
  useEffect(() => {
    document.documentElement.lang = state.language;
  }, [state.language]);

  const loadCredits = useCallback(async () => {
    const out = await interviewApi.credits();
    if (out.ok) dispatch({ type: "credits", balance: out.value.balance });
    else dispatch({ type: "creditsFailed", failure: out.failure });
  }, []);

  useEffect(() => {
    void loadCredits();
  }, [loadCredits]);

  // The result is fetched once, when the interview completes. A failed read is a notice; the person can try again.
  const loadResult = useCallback(async (id: string) => {
    const out = await interviewApi.result(id);
    if (out.ok) dispatch({ type: "resultLoaded", result: out.value });
    else dispatch({ type: "resultFailed", failure: out.failure });
  }, []);

  useEffect(() => {
    if (state.phase !== "result" || state.result !== null || !state.interview || resultRequested.current) return;
    resultRequested.current = true;
    void loadResult(state.interview.id);
  }, [state.phase, state.result, state.interview, loadResult]);

  useEffect(() => {
    if (state.phase !== "result") resultRequested.current = false;
  }, [state.phase]);

  async function start() {
    dispatch({ type: "starting" });
    const out = await interviewApi.start({ language: state.language, tenure: state.tenure });
    if (out.ok) {
      dispatch({ type: "started", started: out.value });
      return;
    }
    dispatch({ type: "failed", failure: out.failure });
  }

  /**
   * A closing or stop turn comes back with the status still `in_progress`: the service makes the record and the tiles after it, and
   * the status turns `completed` or `stopped` when they are ready (plan §10). Poll the state until then, or the page waits forever.
   */
  async function settle(id: string): Promise<InterviewStatus | null> {
    for (let attempt = 0; attempt < SETTLE_ATTEMPTS; attempt++) {
      await new Promise((resolve) => setTimeout(resolve, SETTLE_DELAY_MS));
      const out = await interviewApi.state(id);
      if (!out.ok) return null;
      if (out.value.status !== "in_progress") return out.value.status;
    }
    return null;
  }

  /** Resolves true when the service accepted the reply, so the chat knows whether to keep the typed text. */
  async function send(text: string): Promise<boolean> {
    if (!state.interview) return false;
    const id = state.interview.id;
    dispatch({ type: "sending", text });
    const out = await interviewApi.reply(id, text);
    if (out.ok) {
      dispatch({ type: "replied", reply: out.value });
      if (out.value.status === "in_progress" && (out.value.turn?.kind === "close" || out.value.turn?.kind === "stop")) {
        const ended = await settle(id);
        if (ended === null) dispatch({ type: "failed", failure: { kind: "generic" } });
        else dispatch({ type: "replied", reply: { status: ended } });
      }
      return true;
    }
    dispatch({ type: "failed", failure: out.failure });
    // A session the service lost gives its credit back: the balance shown must be the service's, not the page's.
    if (out.failure.kind === "gone") void loadCredits();
    return false;
  }

  async function remove() {
    if (!state.interview) return;
    const out = await interviewApi.remove(state.interview.id);
    if (!out.ok) {
      dispatch({ type: "failed", failure: { kind: "generic" } });
      return;
    }
    dispatch({ type: "deleted" });
    void loadCredits();
  }

  async function buy() {
    const out = await interviewApi.checkout(1);
    if (!out.ok) {
      dispatch({ type: "creditsFailed", failure: out.failure });
      return;
    }
    // The address was checked to be https by the client. The payment provider's page takes over from here.
    const url = safeCheckoutUrl(out.value.url);
    if (url) window.location.assign(url);
  }

  return (
    <>
      <h1>{copy.title}</h1>
      {state.deleted && state.phase === "start" ? (
        <p className="notice ok" role="status">
          {copy.deleted}
        </p>
      ) : null}
      {state.phase === "start" ? (
        <StartPanel state={state} dispatch={dispatch} copy={copy} onBuy={buy} onRetryCredits={loadCredits} />
      ) : null}
      {state.phase === "consent" ? (
        <ConsentPanel copy={copy} starting={state.starting} notice={state.notice} onStart={start} onBack={() => dispatch({ type: "toStart" })} />
      ) : null}
      {state.phase === "chat" ? (
        <ChatPanel copy={copy} state={state} onSend={send} onDelete={remove} notice={state.notice} />
      ) : null}
      {state.phase === "result" ? (
        <ResultPanel copy={copy} state={state} onDelete={remove} notice={state.notice} onRetry={() => state.interview && loadResult(state.interview.id)} />
      ) : null}
      {state.phase === "ended" ? <EndedPanel copy={copy} state={state} onAgain={() => { dispatch({ type: "toStart" }); void loadCredits(); }} /> : null}
    </>
  );
}
