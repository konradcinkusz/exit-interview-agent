import type { NextResponse } from "next/server";
import type { ConsentStatus } from "./identity";
import { refreshSession, type RefreshDeps } from "./refresh";
import { verifyAccessToken } from "./jwt";
import type { AuthConfig } from "./runtime-config";
import { setConsentMarker, setSession, type Tokens } from "./session";

/** Writes a freshly issued session: both token cookies and the consent marker derived from the same moment. */
export function establishSession(res: NextResponse, tokens: Tokens, consent: ConsentStatus | null, cfg: AuthConfig): void {
  setSession(res, tokens, cfg.secureCookies);
  // Unknown status fails closed: no marker, so the gate sends the user to the consent step, which asks authservice.
  setConsentMarker(res, consent && !consent.required ? consent : null, tokens.expiresIn, cfg.secureCookies);
}

export type Resolved =
  | { kind: "ok"; accessToken: string; rotated?: { tokens: Tokens; consent: ConsentStatus | null } }
  | { kind: "none" }
  | { kind: "unavailable" };

/**
 * The access token to act with. A verified cookie is used as is; a missing or invalid one is replaced by rotating the
 * refresh token (single-flight). "none" means the session is over (no or revoked refresh token); "unavailable" means
 * the identity service could not be asked, which says nothing about the session, so cookies are left alone.
 */
export async function resolveAccessToken(
  cookies: { access?: string; refresh?: string },
  cfg: AuthConfig,
  deps: RefreshDeps & { verify?: typeof verifyAccessToken } = {},
): Promise<Resolved> {
  const verify = deps.verify ?? verifyAccessToken;
  if (cookies.access && (await verify(cookies.access, cfg))) return { kind: "ok", accessToken: cookies.access };
  if (!cookies.refresh) return { kind: "none" };
  const result = await refreshSession(cfg, cookies.refresh, deps);
  if (result.ok) {
    return { kind: "ok", accessToken: result.session.tokens.accessToken, rotated: { tokens: result.session.tokens, consent: result.session.consent } };
  }
  return result.reason === "invalid" ? { kind: "none" } : { kind: "unavailable" };
}

/**
 * Seconds until a token's `exp`, for sizing a cookie that must not outlive it. Reads the payload WITHOUT verifying it:
 * only ever called on a token that was just verified or just issued by authservice.
 */
export function remainingSeconds(accessToken: string, now: number = Date.now()): number {
  try {
    const payload = JSON.parse(Buffer.from(accessToken.split(".")[1] ?? "", "base64url").toString("utf8")) as { exp?: unknown };
    if (typeof payload.exp === "number") return Math.max(1, Math.floor(payload.exp - now / 1000));
  } catch {
    // fall through
  }
  return 60;
}
