// Pure helpers for the "delete a submission" page. The code is a bearer secret (ADR-0029): it is normalised here and sent
// in the X-Receipt-Code header by the page, and appears in no URL, storage or log.

export type ReceiptOutcome =
  | { kind: "uniform" }
  | { kind: "error"; key: "INVALID_RECEIPT_CODE" | "rate_limited" | "unavailable" | "generic"; retryAfter?: number };

/** Strips the whitespace a copy/paste adds (line wraps, leading and trailing spaces). The server decides what is well formed. */
export function normaliseReceiptCode(input: string): string {
  return input.replace(/\s+/g, "");
}

/**
 * The only success is 204, and 204 is the uniform answer: it means "if a record with this code existed, it is deleted now"
 * and nothing more. 400 INVALID_RECEIPT_CODE is a malformed code; 429 is the limiter (body `{error, retryAfter}`).
 */
export function receiptOutcome(status: number, body: unknown, retryAfterHeader?: string | null): ReceiptOutcome {
  if (status === 204) return { kind: "uniform" };
  const b = (body ?? {}) as { code?: unknown; retryAfter?: unknown };
  if (status === 400 && b.code === "INVALID_RECEIPT_CODE") return { kind: "error", key: "INVALID_RECEIPT_CODE" };
  if (status === 429) {
    const fromBody = typeof b.retryAfter === "number" ? b.retryAfter : undefined;
    const fromHeader = retryAfterHeader && /^\d+$/.test(retryAfterHeader) ? Number(retryAfterHeader) : undefined;
    const seconds = fromBody ?? fromHeader;
    return { kind: "error", key: "rate_limited", retryAfter: seconds && seconds > 0 ? Math.min(Math.ceil(seconds), 3600) : undefined };
  }
  if (status === 502 || status === 503 || status === 504) return { kind: "error", key: "unavailable" };
  return { kind: "error", key: "generic" };
}
