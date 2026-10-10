import { describe, expect, it } from "vitest";
import { initialState, reduce, type State } from "./state";

// The page is a small state machine. These tests pin its transitions, in particular the failure statuses (402, 409, 410, 429,
// 503) that must send the person somewhere sensible, and that deleting empties everything the page holds.

const started = {
  id: "int_abcdefgh",
  status: "in_progress" as const,
  language: "en" as const,
  expiresAt: "2026-10-10T12:00:00Z",
  turn: { index: 0, kind: "opening" as const, text: "Hello, I am an AI interviewer." },
};

const result = {
  record: {
    schemaVersion: "1",
    interviewId: "0".repeat(32),
    piiMasked: true,
    context: { tenureBand: "1y_3y" },
    interview: { protocolVersion: "1.2", language: "en", aiDisclosed: true, durationBand: "10m_20m", turnBand: "10_20" },
    topics: {
      onboarding: { status: "no_data" as const, rating: null, confidence: null, quotes: [] },
      management: { status: "no_data" as const, rating: null, confidence: null, quotes: [] },
      growth: { status: "no_data" as const, rating: null, confidence: null, quotes: [] },
      pay_vs_promises: { status: "no_data" as const, rating: null, confidence: null, quotes: [] },
      culture: { status: "no_data" as const, rating: null, confidence: null, quotes: [] },
      reason_for_leaving: { status: "no_data" as const, rating: null, confidence: null, quotes: [] },
    },
  },
  tiles: { items: [], dropped: [], notice: "Drafts." },
  usage: { modelCalls: 3, tokensEstimated: 1200 },
};

function run(state: State, ...actions: Parameters<typeof reduce>[1][]): State {
  return actions.reduce((s, a) => reduce(s, a), state);
}

describe("start screen", () => {
  it("begins on the start screen in English with the credits loading", () => {
    const s = initialState();
    expect(s.phase).toBe("start");
    expect(s.language).toBe("en");
    expect(s.credits).toEqual({ kind: "loading" });
  });

  it("changes language and tenure", () => {
    const s = run(initialState(), { type: "language", language: "pl" }, { type: "tenure", tenure: "gt_10y" });
    expect(s.language).toBe("pl");
    expect(s.tenure).toBe("gt_10y");
  });

  it("does not let a person start with no credit: the balance decides what the screen offers", () => {
    expect(run(initialState(), { type: "credits", balance: 0 }).credits).toEqual({ kind: "ok", balance: 0 });
  });
});

describe("consent and start", () => {
  it("moves to consent, and back", () => {
    const s = run(initialState(), { type: "toConsent" });
    expect(s.phase).toBe("consent");
    expect(run(s, { type: "toStart" }).phase).toBe("start");
  });

  it("opens the chat with the opening turn, and it is the only line before any reply", () => {
    const s = run(initialState(), { type: "toConsent" }, { type: "starting" }, { type: "started", started });
    expect(s.phase).toBe("chat");
    expect(s.interview).toEqual({ id: "int_abcdefgh", expiresAt: "2026-10-10T12:00:00Z" });
    expect(s.lines.map((l) => [l.from, l.text])).toEqual([["interviewer", "Hello, I am an AI interviewer."]]);
    expect(s.pending).toBe(false);
  });

  it("shows 402 as a request to buy, and stays on the start screen", () => {
    const s = run(initialState(), { type: "toConsent" }, { type: "starting" }, { type: "failed", failure: { kind: "payment_required" } });
    expect(s.phase).toBe("start");
    expect(s.notice).toEqual({ kind: "payment_required" });
    expect(s.starting).toBe(false);
  });

  it("shows 409 'in progress' on the consent step, so the person can finish or delete it", () => {
    const s = run(initialState(), { type: "toConsent" }, { type: "starting" }, { type: "failed", failure: { kind: "interview_in_progress" } });
    expect(s.phase).toBe("consent");
    expect(s.notice).toEqual({ kind: "interview_in_progress" });
  });

  it("keeps the rate-limit wait with the notice", () => {
    const s = run(initialState(), { type: "toConsent" }, { type: "starting" }, { type: "failed", failure: { kind: "rate_limited", retryAfter: 9 } });
    expect(s.notice).toEqual({ kind: "rate_limited", retryAfter: 9 });
  });
});

describe("chat", () => {
  const inChat = () => run(initialState(), { type: "toConsent" }, { type: "starting" }, { type: "started", started });

  it("adds your line as soon as you send, and waits for the interviewer", () => {
    const s = run(inChat(), { type: "sending", text: "It was fine." });
    expect(s.pending).toBe(true);
    expect(s.lines.at(-1)).toMatchObject({ from: "you", text: "It was fine." });
  });

  it("appends the next interviewer turn", () => {
    const s = run(inChat(), { type: "sending", text: "It was fine." }, {
      type: "replied",
      reply: { status: "in_progress", turn: { index: 1, kind: "probe", text: "Can you give an example?" } },
    });
    expect(s.pending).toBe(false);
    expect(s.lines.at(-1)).toMatchObject({ from: "interviewer", text: "Can you give an example?", kind: "probe" });
  });

  it("keeps your text and shows the error when a reply is refused (429, 503, 422)", () => {
    for (const failure of [{ kind: "rate_limited" as const }, { kind: "provider_unavailable" as const }, { kind: "reply_invalid" as const }]) {
      const s = run(inChat(), { type: "sending", text: "Keep me." }, { type: "failed", failure });
      expect(s.phase).toBe("chat");
      expect(s.pending).toBe(false);
      expect(s.notice).toEqual(failure);
      expect(s.lines.at(-1)?.text).toBe("Keep me.");
    }
  });

  it("goes to the result when the reply completes the interview", () => {
    const s = run(inChat(), { type: "sending", text: "Done." }, {
      type: "replied",
      reply: { status: "completed", turn: { index: 2, kind: "close", text: "Thank you." }, ending: { reason: "completed" } },
    });
    expect(s.phase).toBe("result");
    expect(s.ended).toBe("completed");
    expect(s.lines.at(-1)?.text).toBe("Thank you.");
  });

  it("ends without a record when the interview was stopped or failed", () => {
    const stopped = run(inChat(), { type: "sending", text: "Stop." }, {
      type: "replied",
      reply: { status: "stopped", turn: { index: 2, kind: "stop", text: "Understood." } },
    });
    expect(stopped.phase).toBe("ended");
    expect(stopped.ended).toBe("stopped");

    const failed = run(inChat(), { type: "sending", text: "Hi." }, { type: "replied", reply: { status: "failed" } });
    expect(failed.phase).toBe("ended");
    expect(failed.ended).toBe("failed");
  });

  it("returns to the start screen when the service says the session is gone (410)", () => {
    const s = run(inChat(), { type: "failed", failure: { kind: "gone" } });
    expect(s.phase).toBe("start");
    expect(s.interview).toBeNull();
    expect(s.lines).toEqual([]);
    expect(s.notice).toEqual({ kind: "gone" });
  });
});

describe("result and deletion", () => {
  const completed = () =>
    run(initialState(), { type: "toConsent" }, { type: "starting" }, { type: "started", started }, { type: "sending", text: "x" }, {
      type: "replied",
      reply: { status: "completed", ending: { reason: "completed" } },
    });

  it("holds the result only after it is loaded", () => {
    expect(completed().result).toBeNull();
    expect(run(completed(), { type: "resultLoaded", result }).result).toEqual(result);
  });

  it("deletion empties everything the page holds and says so", () => {
    const s = run(completed(), { type: "resultLoaded", result }, { type: "deleted" });
    expect(s.phase).toBe("start");
    expect(s.interview).toBeNull();
    expect(s.lines).toEqual([]);
    expect(s.result).toBeNull();
    expect(s.deleted).toBe(true);
  });

  it("a result that cannot be read is a notice, not a crash", () => {
    const s = run(completed(), { type: "resultFailed", failure: { kind: "not_completed" } });
    expect(s.notice).toEqual({ kind: "not_completed" });
    expect(s.result).toBeNull();
  });
});
