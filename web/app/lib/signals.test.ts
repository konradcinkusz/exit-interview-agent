import { describe, expect, it } from "vitest";
import { employerBody, type EmployerBody, insufficientTopic, listBody, okTopic } from "./signals.fixtures";
import {
  DEFAULT_WAIT_SECONDS, fill, formatBatchStart, levelPercent, readEmployer, readEmployerList, readPage, signalsFailure, twoDecimals,
} from "./signals";
import { m } from "./messages";

const clone = <T,>(v: T): T => structuredClone(v);

describe("readEmployer", () => {
  it("reads the whole response exactly as sent", () => {
    const e = readEmployer(employerBody());

    expect(e?.employerRef).toBe("demo-acme");
    expect(e?.respondentsBand).toBe("10-24");
    expect(e?.topics.map((t) => t.topic)).toEqual(["onboarding", "management", "growth", "pay_vs_promises", "culture", "reason_for_leaving"]);
    const first = e?.topics[0];
    expect(first?.status === "ok" && first.overall).toMatchObject({ n: 12, mean: 3.82, lower: 3.4, upper: 4.2, level: 0.95, reliability: "moderate", coverage: "high" });
  });

  it("keeps no number for a topic that says insufficient_data, even if the response carries one", () => {
    const body = clone(employerBody());
    (body.topics[1] as Record<string, unknown>).overall = { n: 3, mean: 4.5, interval: { lower: 4, upper: 5, level: 0.95 }, reliability: "low", coverage: "low" };

    const topic = readEmployer(body)?.topics[1];

    expect(topic).toEqual({ topic: "management", status: "insufficient_data" });
    expect(JSON.stringify(topic)).not.toMatch(/\d/);
  });

  it("keeps no cell of a suppressed cut, whatever the response carried: no band can be named", () => {
    const body = clone(employerBody());
    const cut = (body.topics[0] as { cuts: { cells: unknown[] }[] }).cuts[1]!;
    cut.cells = [{ band: "junior", status: "suppressed", stats: null }];

    const topic = readEmployer(body)?.topics[0];
    const read = topic?.status === "ok" ? topic.cuts[1] : undefined;

    expect(read).toEqual({ dimension: "seniority", status: "suppressed" });
  });

  it.each([
    ["an unknown topic status", (b: EmployerBody) => ((b.topics[0] as { status: string }).status = "maybe")],
    ["an unknown cut status", (b: EmployerBody) => ((b.topics[0] as { cuts: { status: string }[] }).cuts[0]!.status = "partial")],
    ["an unknown band status", (b: EmployerBody) => ((b.topics[0] as { cuts: { cells: { status: string }[] }[] }).cuts[0]!.cells[0]!.status = "tiny")],
    ["an unknown reliability", (b: EmployerBody) => ((b.topics[0] as { overall: { reliability: string } }).overall.reliability = "excellent")],
    ["a missing interval", (b: EmployerBody) => delete (b.topics[0] as { overall?: { interval?: unknown } }).overall!.interval],
    ["a non-integer n", (b: EmployerBody) => ((b.topics[0] as { overall: { n: number } }).overall.n = 12.5)],
    ["a NaN mean", (b: EmployerBody) => ((b.topics[0] as { overall: { mean: number } }).overall.mean = Number.NaN)],
    ["an ok topic without numbers", (b: EmployerBody) => ((b.topics[0] as { overall: unknown }).overall = null)],
    ["a bad snapshot date", (b: EmployerBody) => ((b.snapshot as { generatedAt: string }).generatedAt = "yesterday")],
    ["a missing respondents band", (b: EmployerBody) => ((b as { respondentsBand: unknown }).respondentsBand = undefined)],
    ["a malformed group", (b: EmployerBody) => ((b.topics[0] as { overall: { distribution: unknown } }).overall.distribution = [{ key: "low" }])],
  ])("refuses %s (nothing is rendered that is not understood)", (_name, mutate) => {
    const body = clone(employerBody());
    mutate(body);
    expect(readEmployer(body)).toBeNull();
  });

  it.each([null, undefined, "x", 5, [], {}, { topics: "no" }])("refuses %j", (v) => expect(readEmployer(v)).toBeNull());

  it("does not mutate or reorder: topics come out in the order the API sent them", () => {
    const body = clone(employerBody());
    body.topics = [okTopic("culture"), insufficientTopic("onboarding")] as typeof body.topics;
    expect(readEmployer(body)?.topics.map((t) => t.topic)).toEqual(["culture", "onboarding"]);
  });
});

describe("readEmployerList", () => {
  it("reads the page as sent, in the order sent (no sorting)", () => {
    const list = readEmployerList(listBody(["zeta", "alpha", "mid"]));
    expect(list?.employers).toEqual(["zeta", "alpha", "mid"]);
    expect(list).toMatchObject({ page: 1, limit: 20, total: 3 });
  });

  it("reads the empty state with no snapshot", () => {
    expect(readEmployerList({ snapshot: null, employers: [], page: 1, limit: 20, total: 0 })).toMatchObject({ snapshot: null, employers: [], total: 0 });
  });

  it.each([null, {}, { snapshot: null, employers: [1], page: 1, limit: 20, total: 1 }, listBody(["a"], { total: -1 }), listBody(["a"], { snapshot: { generatedAt: "x" } })])(
    "refuses %j",
    (v) => expect(readEmployerList(v)).toBeNull(),
  );
});

describe("signalsFailure", () => {
  it("treats an unknown employer, one below k, and a malformed reference as the same state", () => {
    expect(signalsFailure(404, { code: "SIGNALS_EMPLOYER_NOT_FOUND" }, null)).toEqual({ kind: "not_found" });
    expect(signalsFailure(400, { code: "SIGNALS_INVALID_EMPLOYER_REF" }, null)).toEqual({ kind: "not_found" });
  });

  it("takes the wait of a 429 from Retry-After, clamps it, and falls back to a calm default", () => {
    expect(signalsFailure(429, { error: "rate_limited", retryAfter: 5 }, "17")).toEqual({ kind: "rate_limited", retryAfter: 17 });
    expect(signalsFailure(429, {}, "999999")).toEqual({ kind: "rate_limited", retryAfter: 3600 });
    for (const bad of [null, "", "0", "-3", "soon", "Wed, 21 Oct 2026 07:28:00 GMT", "1.5"]) {
      expect(signalsFailure(429, {}, bad)).toEqual({ kind: "rate_limited", retryAfter: DEFAULT_WAIT_SECONDS });
    }
  });

  it.each([
    [401, {}, "unauthenticated"], [403, { error: "consent_required" }, "consent_required"], [403, {}, "generic"], [502, {}, "unavailable"],
    [503, { error: "backend_unavailable" }, "unavailable"], [504, {}, "unavailable"], [500, {}, "generic"], [404, { error: "not_found" }, "generic"], [404, null, "generic"],
  ])("%i %j -> %s", (status, body, kind) => expect(signalsFailure(status, body, null).kind).toBe(kind));
});

describe("formatting", () => {
  it("fills placeholders and leaves unknown ones visible", () => {
    expect(fill("a {x} b {y}", { x: 1 })).toBe("a 1 b {y}");
  });

  it("renders the API's numbers to two decimals without any policy", () => {
    expect(twoDecimals(3.8)).toBe("3.80");
    expect(twoDecimals(5)).toBe("5.00");
    expect(levelPercent(0.95)).toBe("95");
  });

  it("shows the batch start in UTC", () => {
    const text = formatBatchStart("2026-10-05T00:00:00+00:00");
    expect(text).toContain("2026");
    expect(text).toContain("UTC");
  });

  it("reads a page number defensively", () => {
    expect([readPage(undefined), readPage("3"), readPage("0"), readPage("-1"), readPage("abc"), readPage("1e3"), readPage(["4", "5"]), readPage("9999999")]).toEqual([1, 3, 1, 1, 1, 1, 4, 1]);
  });
});

describe("the catalog templates (AGGREGATION section 8)", () => {
  const S = m.signals;
  it("has every placeholder the code fills, and the contract wording", () => {
    expect(fill(S.statLine, { mean: "3.82", level: "95", lower: "3.40", upper: "4.20", n: 12 })).toBe("3.82 (95% interval 3.40-4.20), 12 ratings");
    expect(fill(S.snapshot, { date: "D" })).toBe("Updated D. Figures change once per update, not when someone submits. A record deleted after this date is still counted until the next update.");
    expect(S.aggregatedNote).toBe("Aggregated from records submitted by users of this tool. Employment is claimed, not verified.");
    expect(S.wideInterval).toBe("A wide interval means early, not wrong.");
    expect(S.notEnough).toBe("Not enough responses to show this topic.");
    expect(S.cutSuppressed).toBe("This breakdown is hidden to protect small groups.");
    expect(S.listOrder).toBe("Employers are listed alphabetically. This tool does not rank employers.");
    expect(S.coverage.low).toBe("low: the share of respondents who rated this topic.");
    expect(m.deleteSubmission.publishedFigures).toBe("Deleting your record removes it from the stored data now. Published figures drop it at the next update.");
  });

  it("never lets the not-enough message or the suppressed message carry a placeholder (no count, no band)", () => {
    expect(S.notEnough).not.toMatch(/[{}\d]/);
    expect(S.cutSuppressed).not.toMatch(/[{}\d]/);
    expect(S.cutBandSuppressed).not.toMatch(/[{}\d]/);
  });
});
