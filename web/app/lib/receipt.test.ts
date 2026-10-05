import { describe, expect, it } from "vitest";
import { normaliseReceiptCode, receiptOutcome } from "./receipt";

describe("normaliseReceiptCode", () => {
  it("removes the whitespace that copy and paste add, and nothing else", () => {
    expect(normaliseReceiptCode("  abc DEF\n_-9 ")).toBe("abcDEF_-9");
  });
});

describe("receiptOutcome", () => {
  it("treats 204 as the uniform answer and nothing more", () => {
    expect(receiptOutcome(204, null)).toEqual({ kind: "uniform" });
  });

  it("maps 400 INVALID_RECEIPT_CODE to a fixable error", () => {
    expect(receiptOutcome(400, { type: "about:blank", title: "t", status: 400, code: "INVALID_RECEIPT_CODE" })).toEqual({ kind: "error", key: "INVALID_RECEIPT_CODE" });
  });

  it("does not turn an unrecognised 400 into a success or into the code error", () => {
    expect(receiptOutcome(400, { code: "SOMETHING_ELSE" })).toEqual({ kind: "error", key: "generic" });
  });

  it("maps 429 with the retry hint from the body, else the header, bounded", () => {
    expect(receiptOutcome(429, { error: "rate_limited", retryAfter: 30 })).toEqual({ kind: "error", key: "rate_limited", retryAfter: 30 });
    expect(receiptOutcome(429, {}, "12")).toEqual({ kind: "error", key: "rate_limited", retryAfter: 12 });
    expect(receiptOutcome(429, {}, "soon")).toEqual({ kind: "error", key: "rate_limited", retryAfter: undefined });
    expect(receiptOutcome(429, { retryAfter: 1e9 })).toEqual({ kind: "error", key: "rate_limited", retryAfter: 3600 });
  });

  it("maps gateway failures to unavailable, never to success", () => {
    for (const status of [502, 503, 504]) expect(receiptOutcome(status, null)).toEqual({ kind: "error", key: "unavailable" });
  });
});
