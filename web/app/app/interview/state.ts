import type { FailureKind, InterviewFailure } from "@/lib/interview-api";
import type { InterviewLanguage, InterviewResult, InterviewStarted, ReplyResult, Tenure, TurnKind } from "@/lib/interview-contract";

// The page's state, as a pure reducer so its transitions are tested without a browser. It holds the transcript and the result in
// memory only: nothing here is written to storage, a cookie, the URL or a log, and reloading the page empties it (ADR-0048).

export type Phase = "start" | "consent" | "chat" | "result" | "ended";

export interface Line {
  id: number;
  from: "interviewer" | "you";
  text: string;
  kind?: TurnKind;
}

export type Credits = { kind: "loading" } | { kind: "ok"; balance: number } | { kind: "failed"; failure: FailureKind };

export interface State {
  phase: Phase;
  language: InterviewLanguage;
  tenure: Tenure;
  credits: Credits;
  starting: boolean;
  interview: { id: string; expiresAt: string } | null;
  lines: Line[];
  pending: boolean;
  /** The last thing that went wrong, shown with copy next to the control that caused it. */
  notice: InterviewFailure | null;
  /** How the interview ended, for the ended screen and the result: `completed` (a record exists), `stopped` or `failed`. */
  ended: "completed" | "stopped" | "failed" | null;
  result: InterviewResult | null;
  /** Set after a successful deletion, so the start screen can say so once. */
  deleted: boolean;
  nextLine: number;
}

export type Action =
  | { type: "language"; language: InterviewLanguage }
  | { type: "tenure"; tenure: Tenure }
  | { type: "credits"; balance: number }
  | { type: "creditsFailed"; failure: InterviewFailure }
  | { type: "toConsent" }
  | { type: "toStart" }
  | { type: "starting" }
  | { type: "started"; started: InterviewStarted }
  | { type: "sending"; text: string }
  | { type: "replied"; reply: ReplyResult }
  | { type: "failed"; failure: InterviewFailure }
  | { type: "resultLoaded"; result: InterviewResult }
  | { type: "resultFailed"; failure: InterviewFailure }
  | { type: "deleted" };

export function initialState(): State {
  return {
    phase: "start",
    language: "en",
    tenure: "1y_3y",
    credits: { kind: "loading" },
    starting: false,
    interview: null,
    lines: [],
    pending: false,
    notice: null,
    ended: null,
    result: null,
    deleted: false,
    nextLine: 1,
  };
}

function addLine(s: State, from: Line["from"], text: string, kind?: TurnKind): State {
  const line: Line = kind === undefined ? { id: s.nextLine, from, text } : { id: s.nextLine, from, text, kind };
  return { ...s, lines: [...s.lines, line], nextLine: s.nextLine + 1 };
}

export function reduce(s: State, action: Action): State {
  switch (action.type) {
    case "language":
      return { ...s, language: action.language };
    case "tenure":
      return { ...s, tenure: action.tenure };
    case "credits":
      return { ...s, credits: { kind: "ok", balance: action.balance } };
    case "creditsFailed":
      return { ...s, credits: { kind: "failed", failure: action.failure.kind } };
    case "toConsent":
      return { ...s, phase: "consent", notice: null };
    case "toStart":
      return { ...s, phase: "start", notice: null };
    case "starting":
      return { ...s, starting: true, notice: null };
    case "started":
      return {
        ...addLine({ ...s, starting: false, phase: "chat", interview: { id: action.started.id, expiresAt: action.started.expiresAt } }, "interviewer", action.started.turn.text, action.started.turn.kind),
        pending: false,
      };
    case "sending":
      return { ...addLine({ ...s, pending: true, notice: null }, "you", action.text) };
    case "replied": {
      const withTurn = action.reply.turn ? addLine(s, "interviewer", action.reply.turn.text, action.reply.turn.kind) : s;
      const status = action.reply.status;
      if (status === "completed") return { ...withTurn, pending: false, phase: "result", ended: "completed" };
      if (status === "stopped" || status === "failed") return { ...withTurn, pending: false, phase: "ended", ended: status };
      return { ...withTurn, pending: false };
    }
    case "failed": {
      const base = { ...s, pending: false, starting: false, notice: action.failure };
      switch (action.failure.kind) {
        case "gone":
        case "not_found":
          // The service no longer has this interview (410, or 404 after a delete or a restart): the chat is over, a new one can start.
          return { ...base, phase: "start", interview: null, lines: [], result: null };
        case "payment_required":
          return { ...base, phase: "start" };
        case "interview_ended":
          return { ...base, phase: "result", ended: "completed" };
        default:
          return base; // every other failure keeps the person where they are, with the notice beside the control
      }
    }
    case "resultLoaded":
      return { ...s, result: action.result, notice: null };
    case "resultFailed":
      return { ...s, notice: action.failure };
    case "deleted":
      return { ...initialState(), language: s.language, tenure: s.tenure, credits: s.credits, deleted: true };
  }
}
