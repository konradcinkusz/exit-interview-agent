import {
  parseCheckout,
  parseCredits,
  parseReply,
  parseResult,
  parseStarted,
  parseState,
  type Checkout,
  type Credits,
  type InterviewLanguage,
  type InterviewResult,
  type InterviewStarted,
  type InterviewState,
  type ReplyResult,
  type Tenure,
} from "./interview-contract";

// The typed client of the interview endpoints (plan section 10). It talks only to the BFF (`/api/proxy/v1/...`): the BFF holds
// the bearer, the browser never sees a backend address or the service key (FRONTEND-BFF §1). Every request is `no-store`, and
// no answer is kept by this module: an interview is never cached (ADR-0068 covers the signals reads only).
//
// Every call returns an `Outcome`, never throws. A failure carries a stable kind, which the page turns into copy; the raw body
// and the interview text are never placed in the failure, so nothing the page shows can echo them back.

export const INTERVIEW_BASE = "/api/proxy/v1";

export type FailureKind =
  | "payment_required"
  | "rate_limited"
  | "interviews_disabled"
  | "interview_in_progress"
  | "interview_ended"
  | "reply_invalid"
  | "not_found"
  | "gone"
  | "not_completed"
  | "provider_unavailable"
  | "billing_disabled"
  | "consent_required"
  | "email_not_verified"
  | "unauthenticated"
  | "unavailable"
  | "generic";

export interface InterviewFailure {
  kind: FailureKind;
  /** Seconds to wait, from `Retry-After` or the body, for `rate_limited` only. */
  retryAfter?: number;
}

export type Outcome<T> = { ok: true; value: T } | { ok: false; failure: InterviewFailure };

type FetchLike = typeof fetch;

/** The stable `code` of an answer: the problem document's `code`, or the BFF's and the kernel limiter's `error`. */
function codeOf(body: unknown): string | undefined {
  if (typeof body !== "object" || body === null) return undefined;
  const { code, error } = body as { code?: unknown; error?: unknown };
  if (typeof code === "string") return code;
  if (typeof error === "string") return error;
  return undefined;
}

function retryAfterOf(body: unknown, header: string | null): number | undefined {
  const fromHeader = header !== null && /^\d{1,6}$/.test(header.trim()) ? Number(header) : undefined;
  if (fromHeader !== undefined) return fromHeader;
  const fromBody = typeof body === "object" && body !== null ? (body as { retryAfter?: unknown }).retryAfter : undefined;
  return typeof fromBody === "number" && Number.isInteger(fromBody) && fromBody >= 0 ? fromBody : undefined;
}

/** The one place an HTTP answer becomes a failure kind (the mapping of plan section 10's error column). */
export function failureFor(status: number, body: unknown, retryAfterHeader: string | null): InterviewFailure {
  const code = codeOf(body);
  switch (status) {
    case 401:
      return { kind: "unauthenticated" };
    case 402:
      return { kind: "payment_required" };
    case 403:
      if (code === "consent_required") return { kind: "consent_required" };
      if (code === "email_not_verified") return { kind: "email_not_verified" };
      return { kind: "generic" };
    case 404:
      return { kind: "not_found" };
    case 409:
      if (code === "interview_in_progress") return { kind: "interview_in_progress" };
      if (code === "interview_ended") return { kind: "interview_ended" };
      if (code === "not_completed") return { kind: "not_completed" };
      return { kind: "generic" };
    case 410:
      return { kind: "gone" };
    case 422:
      return { kind: "reply_invalid" };
    case 429: {
      const retryAfter = retryAfterOf(body, retryAfterHeader);
      return retryAfter === undefined ? { kind: "rate_limited" } : { kind: "rate_limited", retryAfter };
    }
    case 503:
      if (code === "interviews_disabled") return { kind: "interviews_disabled" };
      if (code === "provider_unavailable") return { kind: "provider_unavailable" };
      if (code === "billing_disabled") return { kind: "billing_disabled" };
      return { kind: "unavailable" };
    case 504:
      return { kind: "unavailable" };
    default:
      return { kind: "generic" };
  }
}

/** A checkout address the browser may be sent to: absolute https only. Anything else is refused, not followed. */
export function safeCheckoutUrl(value: unknown): string | null {
  if (typeof value !== "string") return null;
  try {
    const url = new URL(value);
    return url.protocol === "https:" ? url.href : null;
  } catch {
    return null;
  }
}

async function call<T>(
  fetchImpl: FetchLike,
  method: "GET" | "POST" | "DELETE",
  path: string,
  parse: (body: unknown) => T | null,
  body?: unknown,
): Promise<Outcome<T>> {
  const init: RequestInit = { method, cache: "no-store", headers: { accept: "application/json" } };
  if (body !== undefined) {
    init.headers = { accept: "application/json", "content-type": "application/json" };
    init.body = JSON.stringify(body);
  }
  let response: Response;
  try {
    response = await fetchImpl(`${INTERVIEW_BASE}${path}`, init);
  } catch {
    return { ok: false, failure: { kind: "unavailable" } };
  }
  if (response.status === 204) return { ok: true, value: undefined as T };
  const data = (await response.json().catch(() => null)) as unknown;
  if (response.status >= 200 && response.status < 300) {
    const value = parse(data);
    return value === null ? { ok: false, failure: { kind: "generic" } } : { ok: true, value };
  }
  return { ok: false, failure: failureFor(response.status, data, response.headers.get("retry-after")) };
}

export const interviewApi = {
  /** Consumes one credit and opens the interview; the answer is the opening turn. */
  start(input: { language: InterviewLanguage; tenure: Tenure }, fetchImpl: FetchLike = fetch): Promise<Outcome<InterviewStarted>> {
    return call(fetchImpl, "POST", "/interviews", parseStarted, input);
  },

  reply(id: string, text: string, fetchImpl: FetchLike = fetch): Promise<Outcome<ReplyResult>> {
    return call(fetchImpl, "POST", `/interviews/${encodeURIComponent(id)}/reply`, parseReply, { text });
  },

  state(id: string, fetchImpl: FetchLike = fetch): Promise<Outcome<InterviewState>> {
    return call(fetchImpl, "GET", `/interviews/${encodeURIComponent(id)}`, parseState);
  },

  result(id: string, fetchImpl: FetchLike = fetch): Promise<Outcome<InterviewResult>> {
    return call(fetchImpl, "GET", `/interviews/${encodeURIComponent(id)}/result`, parseResult);
  },

  /** Withdraw and wipe: the transcript and any result are removed on the service. */
  remove(id: string, fetchImpl: FetchLike = fetch): Promise<Outcome<void>> {
    return call(fetchImpl, "DELETE", `/interviews/${encodeURIComponent(id)}`, () => undefined);
  },

  credits(fetchImpl: FetchLike = fetch): Promise<Outcome<Credits>> {
    return call(fetchImpl, "GET", "/credits", parseCredits);
  },

  /** A provider-hosted payment page. The caller navigates to it; this module does not follow it. */
  async checkout(quantity: number, fetchImpl: FetchLike = fetch): Promise<Outcome<Checkout>> {
    const out = await call(fetchImpl, "POST", "/checkout", parseCheckout, { quantity });
    if (!out.ok) return out;
    const url = safeCheckoutUrl(out.value.url);
    return url === null ? { ok: false, failure: { kind: "generic" } } : { ok: true, value: { url } };
  },
};
