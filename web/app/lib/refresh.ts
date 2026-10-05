import { createHash } from "node:crypto";
import { callRefresh, fetchConsentStatus, type ConsentStatus, type Fetch } from "./identity";
import type { AuthConfig } from "./runtime-config";
import type { Tokens } from "./session";

// Refresh-token rotation, single-flight (IDENTITY-AND-ACCOUNTS §2, FRONTEND-BFF §3). authservice rotates: a refresh
// token is single-use, and presenting a used one is treated as theft and revokes the whole family. A browser fires
// several requests at once (page, config, proxy calls), all carrying the same old cookie, so without coordination
// the second call would present an already-consumed token and end the session.
//
// So one refresh per refresh token per process: concurrent callers share the in-flight promise, and callers that
// arrive within RECENT_MS (carrying the cookie the browser had not yet replaced) get the same result. Several
// instances do not share this map; the platform keeps one web machine per environment today, and ADR-0013 records
// the residual risk and the trigger for a shared store.

export interface RefreshedSession {
  tokens: Tokens;
  /** The consent status read with the NEW token, so the marker cookie is re-evaluated at every rotation. */
  consent: ConsentStatus | null;
}

export type SessionRefresh = { ok: true; session: RefreshedSession } | { ok: false; reason: "invalid" | "unavailable" };

const RECENT_MS = 15_000;
const MAX_ENTRIES = 500;

interface Entry {
  promise: Promise<SessionRefresh>;
  /** Set when the promise settles with success; failures are dropped immediately so a retry can succeed. */
  until?: number;
}

const entries = new Map<string, Entry>();

const keyOf = (refreshToken: string) => createHash("sha256").update(refreshToken).digest("hex");

export interface RefreshDeps {
  fetch?: Fetch;
  now?: () => number;
}

export function refreshSession(cfg: AuthConfig, refreshToken: string, deps: RefreshDeps = {}): Promise<SessionRefresh> {
  const now = deps.now ?? Date.now;
  const key = keyOf(refreshToken);
  prune(now());

  const existing = entries.get(key);
  if (existing) return existing.promise;

  const entry: Entry = {
    promise: (async (): Promise<SessionRefresh> => {
      const outcome = await callRefresh(cfg, refreshToken, deps.fetch);
      if (!outcome.ok) return outcome;
      const consent = await fetchConsentStatus(cfg, outcome.tokens.accessToken, deps.fetch);
      return { ok: true, session: { tokens: outcome.tokens, consent } };
    })(),
  };
  entries.set(key, entry);
  void entry.promise.then((result) => {
    if (result.ok) entry.until = now() + RECENT_MS;
    else entries.delete(key);
  });
  return entry.promise;
}

function prune(at: number) {
  for (const [key, entry] of entries) {
    if (entry.until !== undefined && entry.until <= at) entries.delete(key);
  }
  while (entries.size > MAX_ENTRIES) {
    const oldest = entries.keys().next().value;
    if (oldest === undefined) break;
    entries.delete(oldest);
  }
}

/** Test seam: drops all state. */
export function resetRefreshState() {
  entries.clear();
}
