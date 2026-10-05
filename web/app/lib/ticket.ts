// Pure helpers for the ticket page: mapping the backend's answers to catalog keys, and the countdown. Kept out of the
// component so they are unit-tested without a DOM. Nothing here touches a ticket value except `parseTicket`, which does
// not log or store it.

export type TicketErrorKey = "TICKET_LIMIT" | "rate_limited" | "unauthenticated" | "consent_required" | "unavailable" | "generic";

export interface Minted {
  ticket: string;
  expiresAt: number; // epoch milliseconds
}

/** `{ticket, expiresAt}` from `POST /api/v1/tickets` (T5 contract, 201). Anything else is not a ticket. */
export function parseTicket(body: unknown): Minted | null {
  const b = body as { ticket?: unknown; expiresAt?: unknown } | null;
  if (typeof b?.ticket !== "string" || b.ticket.length === 0 || typeof b.expiresAt !== "string") return null;
  const at = Date.parse(b.expiresAt);
  return Number.isNaN(at) ? null : { ticket: b.ticket, expiresAt: at };
}

/**
 * Maps a non-201 answer to a catalog key. The backend speaks problem+json (`code`: TICKET_LIMIT) for the per-account caps
 * and the kernel's `{error: "rate_limited", retryAfter}` for the HTTP limiter; the BFF adds `{error}` for its own refusals.
 */
export function ticketErrorKey(status: number, body: unknown): { key: TicketErrorKey; retryAfter?: number } {
  const b = (body ?? {}) as { code?: unknown; error?: unknown; retryAfter?: unknown };
  const retryAfter = typeof b.retryAfter === "number" && b.retryAfter > 0 ? Math.min(Math.ceil(b.retryAfter), 3600) : undefined;
  if (b.code === "TICKET_LIMIT") return { key: "TICKET_LIMIT" };
  if (status === 429) return { key: "rate_limited", retryAfter };
  if (status === 401) return { key: "unauthenticated" };
  if (status === 403 && b.error === "consent_required") return { key: "consent_required" };
  if (status === 502 || status === 503 || status === 504) return { key: "unavailable" };
  return { key: "generic" };
}

/** "m:ss" for a remaining time; never negative. */
export function formatRemaining(ms: number): string {
  const total = Math.max(0, Math.ceil(ms / 1000));
  const minutes = Math.floor(total / 60);
  const seconds = total % 60;
  return `${minutes}:${seconds.toString().padStart(2, "0")}`;
}
