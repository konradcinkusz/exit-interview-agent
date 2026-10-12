import { readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import {
  CONTRACT_ERROR_CODES,
  parseCheckout,
  parseCredits,
  parseDraft,
  parseReply,
  parseResult,
  parseStarted,
  parseState,
} from "./interview-contract";

// The golden samples the service's tests write and compare (tests/ExitInterviewAgent.InterviewService.Tests/Contracts). This file
// parses the SAME files with the BFF's typed parsers, so a drift in either language fails a test on that side.
const CONTRACTS = path.resolve(import.meta.dirname, "../../../tests/contracts");
const sample = (name: string): unknown => JSON.parse(readFileSync(path.join(CONTRACTS, `${name}.json`), "utf8"));

/** A fresh copy of the golden result, so each test can change its tiles without touching the others. */
const resultSample = (): Record<string, unknown> => JSON.parse(JSON.stringify(sample("interview-result"))) as Record<string, unknown>;
const withTiles = (change: (tiles: Record<string, unknown>) => void): unknown => {
  const result = resultSample();
  change(result.tiles as Record<string, unknown>);
  return result;
};

/** A structured-review draft as the service sends it (platform-drafts-spec §3.4). */
const draftSample = (): Record<string, unknown> => ({
  platform: "gowork",
  name: "GoWork.pl",
  family: "structured_review",
  risk: "high",
  url: "https://www.gowork.pl/",
  warning: "To szkic, nie fakt. To nie jest porada prawna.",
  sections: [{ key: "pros", label: "Plusy", text: "Dobry zespół i jasne zasady." }],
  copyText: "Dobry zespół i jasne zasady.",
  fieldsOnSite: ["Status zatrudnienia"],
});

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
    expect(result?.tiles.drafts).toEqual([]);
    expect(result?.usage).toEqual({ modelCalls: 4, tokensEstimated: 5400 });
    expect(result?.record.topics.onboarding).toMatchObject({ status: "covered", rating: 2 });
    expect(parseCredits(sample("credits"))).toEqual({ balance: 1 });
    expect(parseCheckout(sample("checkout"))).toEqual({ url: "https://checkout.example.invalid/session/sample" });
  });
});

describe("tiles: unknown kinds and missing drafts (ADR-0083)", () => {
  it("an item of a kind this page does not know is skipped and the rest of the result is kept", () => {
    const parsed = parseResult(
      withTiles((tiles) => {
        tiles.items = [
          { kind: "structured_review", text: "A family kind the page has no card for." },
          { kind: "overview", text: "Kept." },
          { kind: "future_kind", text: "Skipped." },
          { kind: "future_kind", nothing: 1 },
        ];
      }),
    );
    expect(parsed).not.toBeNull();
    expect(parsed?.tiles.items).toEqual([{ kind: "overview", text: "Kept." }]);
  });

  it("a known kind with a text that is not a string still fails the result", () => {
    expect(parseResult(withTiles((tiles) => (tiles.items = [{ kind: "overview", text: 3 }])))).toBeNull();
  });

  it("a result without drafts has none", () => {
    const parsed = parseResult(
      withTiles((tiles) => {
        delete tiles.drafts;
      }),
    );
    expect(parsed?.tiles.drafts).toEqual([]);
  });

  it("drafts that are not a list fail the result", () => {
    expect(parseResult(withTiles((tiles) => (tiles.drafts = {})))).toBeNull();
    expect(parseResult(withTiles((tiles) => (tiles.drafts = null)))).toBeNull();
  });
});

describe("tiles: platform drafts", () => {
  it("a well-formed draft parses, with its sections, copy text and fields on the site", () => {
    const parsed = parseResult(withTiles((tiles) => (tiles.drafts = [draftSample()])));
    expect(parsed?.tiles.drafts).toEqual([
      {
        platform: "gowork",
        name: "GoWork.pl",
        family: "structured_review",
        risk: "high",
        url: "https://www.gowork.pl/",
        warning: "To szkic, nie fakt. To nie jest porada prawna.",
        sections: [{ key: "pros", label: "Plusy", text: "Dobry zespół i jasne zasady." }],
        copyText: "Dobry zespół i jasne zasady.",
        fieldsOnSite: ["Status zatrudnienia"],
      },
    ]);
  });

  it.each([
    ["an unknown family", { family: "blog_post" }],
    ["an unknown risk word", { risk: "severe" }],
    ["a link that is not https", { url: "http://www.gowork.pl/" }],
    ["a link that is a javascript: URL", { url: "javascript:alert(1)" }],
    ["a missing warning", { warning: undefined }],
    ["a missing name", { name: undefined }],
    ["a section without its text", { sections: [{ key: "pros", label: "Plusy" }] }],
    ["fields on the site that are not strings", { fieldsOnSite: [1] }],
  ])("a draft with %s makes the whole result null", (_label, change) => {
    const draft = { ...draftSample(), ...change };
    expect(parseDraft(draft)).toBeNull();
    expect(parseResult(withTiles((tiles) => (tiles.drafts = [draft])))).toBeNull();
  });

  it("one malformed draft among good ones fails the result, never silently drops a draft", () => {
    expect(parseResult(withTiles((tiles) => (tiles.drafts = [draftSample(), { ...draftSample(), family: "unknown" }])))).toBeNull();
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
