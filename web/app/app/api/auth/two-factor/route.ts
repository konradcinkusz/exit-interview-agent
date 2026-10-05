import { cookies } from "next/headers";
import { NextResponse } from "next/server";
import { fetchConsentStatus, loginWithTwoFactor, type TwoFactorOutcome } from "@/lib/identity";
import { authConfig, identityConfigured } from "@/lib/runtime-config";
import { TWO_FACTOR_COOKIE, clearTwoFactorChallenge } from "@/lib/session";
import { establishSession } from "@/lib/session-flow";

export const dynamic = "force-dynamic";

const NO_STORE = { "Cache-Control": "no-store" };
const MAX_CODE = 64;
const STATUS: Record<Exclude<TwoFactorOutcome, { ok: true }>["error"], number> = {
  invalid_code: 401,
  challenge_expired: 401,
  locked: 423,
  rate_limited: 429,
  identity_unavailable: 502,
};

/**
 * Second step of a sign-in (authservice `POST /api/v1/auth/2fa/login`). The challenge comes from the HttpOnly cookie the
 * password step set, never from the page; the body carries only a one-time `code` or a single-use `recoveryCode`.
 * A wrong code keeps the challenge (the user may retry; authservice counts failures toward lockout); a dead challenge,
 * a lockout or success clears it.
 */
export async function POST(request: Request) {
  const cfg = authConfig();
  if (!identityConfigured(cfg)) return NextResponse.json({ error: "identity_not_configured" }, { status: 503, headers: NO_STORE });

  const challenge = (await cookies()).get(TWO_FACTOR_COOKIE)?.value;
  if (!challenge) return NextResponse.json({ error: "challenge_expired" }, { status: 401, headers: NO_STORE });

  const body = (await request.json().catch(() => null)) as { code?: unknown; recoveryCode?: unknown } | null;
  const code = typeof body?.code === "string" ? body.code.trim() : "";
  const recoveryCode = typeof body?.recoveryCode === "string" ? body.recoveryCode.trim() : "";
  if ((!code && !recoveryCode) || (code && recoveryCode) || code.length > MAX_CODE || recoveryCode.length > MAX_CODE) {
    return NextResponse.json({ error: "invalid_request" }, { status: 400, headers: NO_STORE });
  }

  const outcome = await loginWithTwoFactor(cfg, challenge, code ? { code } : { recoveryCode });
  if (!outcome.ok) {
    const response = NextResponse.json({ error: outcome.error }, { status: STATUS[outcome.error], headers: NO_STORE });
    if (outcome.error === "challenge_expired" || outcome.error === "locked") clearTwoFactorChallenge(response, cfg.secureCookies);
    return response;
  }

  const consent = await fetchConsentStatus(cfg, outcome.tokens.accessToken);
  const response = NextResponse.json({ authenticated: true, consentRequired: !consent || consent.required }, { headers: NO_STORE });
  establishSession(response, outcome.tokens, consent, cfg);
  clearTwoFactorChallenge(response, cfg.secureCookies);
  return response;
}
