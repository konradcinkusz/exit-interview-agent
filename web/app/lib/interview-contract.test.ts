import { readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import {
  CONTRACT_ERROR_CODES,
  parseCheckout,
  parseCredits,
  parseReply,
  parseResult,
  parseStarted,
  parseState,
} from "./interview-contract";

// The golden samples the service's tests write and compare (tests/ExitInterviewAgent.InterviewService.Tests/Contracts). This file
// parses the SAME files with the BFF's typed parsers, so a drift in either language fails a test on that side.
const CONTRACTS = path.resolve(import.meta.dirname, "../../../tests/contracts");
const sample = (name: string): unknown => JSON.parse(readFileSync(path.join(CONTRACTS, `${name}.json`), "utf8"));

describe("the golden contract samples parse with the BFF parsers", () => {
  it("the opening turn of POST /interviews", () => {
    const started = parseStarted(sample("interview-started"));
    expect(started).not.toBeNull();
    expect(started?.turn.kind).toBe("opening");
    expect(started?.language).toBe("en");
  });

  it.each(["interview-reply-topic", "interview-reply-close"])("an ordinary reply with null ending parses: %s", (name) => {
    const reply = parseReply(sample(name));
    expect(reply).not.toBeNull();
    expect(reply?.status).toBe("in_progress");
    expect(reply?.ending).toBeUndefined();
  });

  it("a stopped reply carries its ending", () => {
    expect(parseReply(sample("interview-reply-stopped"))).toMatchObject({ status: "stopped", turn: { kind: "stop" }, ending: { reason: "consent_withdrawn" } });
  });

  it("the state, the result with tiles and the record, the credits and the checkout", () => {
    expect(parseState(sample("interview-state"))).toMatchObject({ status: "completed", turnCount: 4 });
    const result = parseResult(sample("interview-result"));
    expect(result).not.toBeNull();
    expect(result?.tiles.items.map((t) => t.kind)).toEqual(["glassdoor", "short_note"]);
    expect(result?.tiles.dropped).toEqual([{ code: "too_long" }]);
    expect(result?.usage).toEqual({ modelCalls: 4, tokensEstimated: 5400 });
    expect(result?.record.topics.onboarding).toMatchObject({ status: "covered", rating: 2 });
    expect(parseCredits(sample("credits"))).toEqual({ balance: 1 });
    expect(parseCheckout(sample("checkout"))).toEqual({ url: "https://checkout.example.invalid/session/sample" });
  });
});

describe("the stable codes match the golden list", () => {
  it("codes.json equals the BFF's list, in both directions", () => {
    const golden = (sample("codes") as { codes: string[] }).codes;
    expect([...CONTRACT_ERROR_CODES].sort()).toEqual([...golden].sort());
  });

  it("every golden file is a sample this test reads", () => {
    const names = readdirSync(CONTRACTS).filter((f) => f.endsWith(".json")).map((f) => f.replace(/\.json$/, ""));
    expect(names.sort()).toEqual(
      ["checkout", "codes", "credits", "interview-reply-close", "interview-reply-stopped", "interview-reply-topic", "interview-result", "interview-started", "interview-state"].sort(),
    );
  });
});
