import { describe, expect, it } from "vitest";
import { formatRemaining, parseTicket, ticketErrorKey } from "./ticket";

describe("parseTicket", () => {
  it("reads the T5 201 body", () => {
    expect(parseTicket({ ticket: "t".repeat(43), expiresAt: "2026-10-05T12:20:00+00:00" })).toEqual({
      ticket: "t".repeat(43),
      expiresAt: Date.parse("2026-10-05T12:20:00Z"),
    });
  });

  it("refuses anything that is not a ticket", () => {
    for (const body of [null, {}, { ticket: "" , expiresAt: "2026-10-05T12:00:00Z" }, { ticket: "x" }, { ticket: "x", expiresAt: "soon" }, { ticket: 5, expiresAt: "2026-10-05T12:00:00Z" }]) {
      expect(parseTicket(body)).toBeNull();
    }
  });
});

describe("ticketErrorKey", () => {
  it("recognises the per-account cap by its stable code, not by status alone", () => {
    expect(ticketErrorKey(429, { type: "about:blank", title: "x", status: 429, code: "TICKET_LIMIT" })).toEqual({ key: "TICKET_LIMIT" });
  });

  it("maps the kernel limiter's body and keeps its retry hint, bounded", () => {
    expect(ticketErrorKey(429, { error: "rate_limited", retryAfter: 42 })).toEqual({ key: "rate_limited", retryAfter: 42 });
    expect(ticketErrorKey(429, { error: "rate_limited", retryAfter: 999999 })).toEqual({ key: "rate_limited", retryAfter: 3600 });
    expect(ticketErrorKey(429, {})).toEqual({ key: "rate_limited", retryAfter: undefined });
  });

  it("maps the BFF's own refusals", () => {
    expect(ticketErrorKey(401, { error: "unauthenticated" }).key).toBe("unauthenticated");
    expect(ticketErrorKey(403, { error: "consent_required" }).key).toBe("consent_required");
    expect(ticketErrorKey(503, { error: "backend_unavailable" }).key).toBe("unavailable");
    expect(ticketErrorKey(500, null).key).toBe("generic");
  });
});

describe("formatRemaining", () => {
  it("formats m:ss and never goes negative", () => {
    expect(formatRemaining(15 * 60_000)).toBe("15:00");
    expect(formatRemaining(61_001)).toBe("1:02");
    expect(formatRemaining(9_000)).toBe("0:09");
    expect(formatRemaining(-5)).toBe("0:00");
  });
});
