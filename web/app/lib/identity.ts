import type { AuthConfig } from "./runtime-config";
import type { Tokens } from "./session";

// The BFF's only conversation with authservice, server side. Every function takes the fetch to use so the
// behaviour is unit-testable without a network. Nothing here logs: tokens, emails and passwords pass through
// and are never written anywhere (brief §6).

export type Fetch = typeof fetch;

const TIMEOUT_MS = 10_000;

const identityUrl = (cfg: AuthConfig, path: string) => `${cfg.url}/api/v1/auth${path}`;

const authorized = (token: string, extra: Record<string, string> = {}) => ({ authorization: `Bearer ${token}`, ...extra });

export type RefreshOutcome =
  | { ok: true; tokens: Tokens }
  /** The refresh token is unknown, expired, already used or revoked: the session is over. */
  | { ok: false; reason: "invalid" }
  /** authservice could not be reached or errored: not a verdict on the session, so cookies stay. */
  | { ok: false; reason: "unavailable" };

/** One call to `POST /api/v1/auth/refresh`: the presented token is consumed and a new pair comes back. */
export async function callRefresh(cfg: AuthConfig, refreshToken: string, fetchImpl: Fetch = fetch): Promise<RefreshOutcome> {
  let upstream: Response;
  try {
    upstream = await fetchImpl(identityUrl(cfg, "/refresh"), {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ refreshToken }),
      redirect: "manual",
      signal: AbortSignal.timeout(TIMEOUT_MS),
    });
  } catch {
    return { ok: false, reason: "unavailable" };
  }
  if (upstream.status === 401 || upstream.status === 400) return { ok: false, reason: "invalid" };
  if (!upstream.ok) return { ok: false, reason: "unavailable" };
  const data = (await upstream.json().catch(() => null)) as
    | { accessToken?: string; refreshToken?: string; expiresIn?: number }
    | null;
  if (!data?.accessToken || !data.refreshToken) return { ok: false, reason: "unavailable" };
  return { ok: true, tokens: { accessToken: data.accessToken, refreshToken: data.refreshToken, expiresIn: data.expiresIn ?? 3600 } };
}

export interface ConsentStatus {
  /** True while the current Terms or Privacy version has not been accepted (cookie consent is not gating). */
  required: boolean;
  /** Versions in force, shown on the consent step and recorded in the marker cookie. */
  terms: string;
  privacy: string;
}

/** `GET /api/v1/auth/consents`: null when the status could not be determined (callers fail closed). */
export async function fetchConsentStatus(cfg: AuthConfig, accessToken: string, fetchImpl: Fetch = fetch): Promise<ConsentStatus | null> {
  try {
    const upstream = await fetchImpl(identityUrl(cfg, "/consents"), {
      headers: authorized(accessToken, { accept: "application/json" }),
      redirect: "manual",
      signal: AbortSignal.timeout(TIMEOUT_MS),
    });
    if (!upstream.ok) return null;
    return parseConsentStatus(await upstream.json());
  } catch {
    return null;
  }
}

/** authservice's `ConsentStatusResponse`, camelCased by the JSON serializer. Anything unexpected is "unknown". */
export function parseConsentStatus(body: unknown): ConsentStatus | null {
  const b = body as {
    terms?: { requiredVersion?: unknown };
    privacy?: { requiredVersion?: unknown };
    requiresConsent?: unknown;
  } | null;
  if (typeof b?.requiresConsent !== "boolean") return null;
  const terms = b.terms?.requiredVersion;
  const privacy = b.privacy?.requiredVersion;
  if (typeof terms !== "string" || typeof privacy !== "string") return null;
  return { required: b.requiresConsent, terms, privacy };
}

/** `POST /api/v1/auth/consents` accepting the CURRENT Terms and Privacy versions: the server records which. */
export async function acceptCurrentConsents(
  cfg: AuthConfig,
  accessToken: string,
  locale: string | undefined,
  fetchImpl: Fetch = fetch,
): Promise<ConsentStatus | null> {
  try {
    const upstream = await fetchImpl(identityUrl(cfg, "/consents"), {
      method: "POST",
      headers: authorized(accessToken, { "content-type": "application/json" }),
      body: JSON.stringify({ acceptedTerms: true, acceptedPrivacy: true, locale }),
      redirect: "manual",
      signal: AbortSignal.timeout(TIMEOUT_MS),
    });
    if (!upstream.ok) return null;
    return parseConsentStatus(await upstream.json());
  } catch {
    return null;
  }
}

/** First language tag of Accept-Language, bounded and stripped: it is stored in the acceptance record. */
export function localeFrom(acceptLanguage: string | null): string | undefined {
  const tag = acceptLanguage?.split(",")[0]?.split(";")[0]?.trim() ?? "";
  return /^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8}){0,2}$/.test(tag) ? tag : undefined;
}

export type DeleteOutcome =
  | { ok: true }
  | { ok: false; error: "password_required" | "invalid_password" | "confirmation_required" | "unauthenticated" | "identity_unavailable" };

/**
 * `DELETE /api/v1/auth/account`: soft-deletes the account at authservice, which revokes every session. It does not
 * and cannot touch any submitted record: records carry no account reference (ADR-0013).
 */
export async function deleteAccount(
  cfg: AuthConfig,
  accessToken: string,
  input: { password?: string; confirmation: string },
  fetchImpl: Fetch = fetch,
): Promise<DeleteOutcome> {
  let upstream: Response;
  try {
    upstream = await fetchImpl(identityUrl(cfg, "/account"), {
      method: "DELETE",
      headers: authorized(accessToken, { "content-type": "application/json" }),
      body: JSON.stringify({ password: input.password || null, confirmation: input.confirmation }),
      redirect: "manual",
      signal: AbortSignal.timeout(TIMEOUT_MS),
    });
  } catch {
    return { ok: false, error: "identity_unavailable" };
  }
  if (upstream.ok) return { ok: true };
  if (upstream.status === 401) return { ok: false, error: "unauthenticated" };
  const detail = ((await upstream.json().catch(() => null)) as { error?: string; errors?: Record<string, unknown> } | null) ?? {};
  if (upstream.status === 400) {
    if (detail.error === "Password is required") return { ok: false, error: "password_required" };
    if (detail.error === "Invalid password") return { ok: false, error: "invalid_password" };
    return { ok: false, error: "confirmation_required" };
  }
  return { ok: false, error: "identity_unavailable" };
}

/** Best-effort `POST /api/v1/auth/logout`: revokes every refresh token of the account. Never throws. */
export async function revokeSessions(cfg: AuthConfig, accessToken: string, fetchImpl: Fetch = fetch): Promise<boolean> {
  try {
    const upstream = await fetchImpl(identityUrl(cfg, "/logout"), {
      method: "POST",
      headers: authorized(accessToken),
      redirect: "manual",
      signal: AbortSignal.timeout(TIMEOUT_MS),
    });
    return upstream.ok;
  } catch {
    return false;
  }
}
