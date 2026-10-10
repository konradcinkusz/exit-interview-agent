import type { BackendName } from "./runtime-config";
import { isEmployerRef } from "./signals-ref";

export interface Route {
  backend: BackendName;
  /** Path on the backend, without query string. */
  upstreamPath: string;
  /**
   * The two read-only Signals endpoints. Their answers may be kept by the browser for as long as the service says (private,
   * until the next batch) and revalidated with the ETag; every other BFF answer is `no-store` (ADR-0068).
   */
  signalsRead: boolean;
}

/**
 * Prefix -> backend routing for the catch-all proxy (FRONTEND-BFF §5), so the browser has exactly one
 * base URL: `/api/proxy/v1/...` is the interview-service's `/api/v1/...`. Only versioned API paths are
 * reachable: health, OpenAPI and anything else on the backend are not exposed through the BFF.
 */
export function routeFor(segments: string[]): Route | null {
  if (segments.length < 2 || segments[0] !== "v1") return null;
  if (segments.some((s) => s === "" || s === "." || s === ".." || s.includes("\\") || s.includes("%"))) return null;
  return { backend: "interview-service", upstreamPath: `/api/${segments.join("/")}`, signalsRead: isSignalsRead(segments) };
}

/** An interview id as the service issues it: random, URL-safe, bounded. Anything else is not an interview path. */
const INTERVIEW_ID = /^[A-Za-z0-9_-]{1,128}$/;

/**
 * The shapes of the interview API the page may call (plan section 10), listed so a path outside them is refused by the tests that
 * pin the contract: `/v1/interviews`, `/v1/interviews/{id}`, `/{id}/reply`, `/{id}/result`, `/v1/credits`, `/v1/checkout`. The catch-all
 * still forwards every `v1/*` path (routeFor); none of these is cacheable, and the webhook is never reached through the BFF.
 */
export function isInterviewPath(segments: string[]): boolean {
  if (segments[0] !== "v1") return false;
  const [head, id, action, ...extra] = segments.slice(1);
  if (extra.length > 0) return false;
  if (head === "credits" || head === "checkout") return id === undefined;
  if (head !== "interviews") return false;
  if (id === undefined) return true;
  if (!INTERVIEW_ID.test(id)) return false;
  return action === undefined || action === "reply" || action === "result";
}

/** `v1/signals/employers` and `v1/signals/employers/{ref}`: nothing else under signals is cacheable. */
function isSignalsRead(segments: string[]): boolean {
  return segments[1] === "signals" && segments[2] === "employers" && (segments.length === 3 || segments.length === 4);
}

/** A signals employer path whose reference is not well formed: answered by the BFF with the API's own 400, without a call upstream. */
export function hasInvalidEmployerRef(segments: string[]): boolean {
  return segments[1] === "signals" && segments[2] === "employers" && segments.length === 4 && !isEmployerRef(segments[3]);
}
