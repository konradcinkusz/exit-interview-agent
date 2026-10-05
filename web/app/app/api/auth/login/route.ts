import { NextResponse } from "next/server";
import { authConfig, identityConfigured } from "@/lib/runtime-config";
import { fetchConsentStatus } from "@/lib/identity";
import { clearTwoFactorChallenge, setTwoFactorChallenge } from "@/lib/session";
import { establishSession } from "@/lib/session-flow";

export const dynamic = "force-dynamic";

const MAX_FIELD = 256;

/**
 * Server-side credential exchange: the browser posts to its own origin, this route calls authservice
 * and sets the HttpOnly cookies. The browser never learns the identity service's address and never
 * holds a token. This account is an account only: it carries no link to any employer.
 */
export async function POST(request: Request) {
  const cfg = authConfig();
  if (!identityConfigured(cfg)) {
    return NextResponse.json({ error: "identity_not_configured" }, { status: 503 });
  }

  const body = (await request.json().catch(() => null)) as { email?: unknown; password?: unknown } | null;
  const email = typeof body?.email === "string" ? body.email.trim() : "";
  const password = typeof body?.password === "string" ? body.password : "";
  if (!email || !password || email.length > MAX_FIELD || password.length > MAX_FIELD) {
    return NextResponse.json({ error: "invalid_request" }, { status: 400 });
  }

  let upstream: Response;
  try {
    upstream = await fetch(`${cfg.url}/api/v1/auth/login`, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ email, password }),
      redirect: "manual",
      signal: AbortSignal.timeout(10_000),
    });
  } catch {
    return NextResponse.json({ error: "identity_unreachable" }, { status: 502 });
  }

  if (upstream.status === 401) return NextResponse.json({ error: "invalid_credentials" }, { status: 401 });
  if (upstream.status === 403) return NextResponse.json({ error: "email_not_verified" }, { status: 403 });
  if (upstream.status === 429) return NextResponse.json({ error: "rate_limited" }, { status: 429 });
  if (!upstream.ok) return NextResponse.json({ error: "identity_error" }, { status: 502 });

  const data = (await upstream.json().catch(() => null)) as
    | { accessToken?: string; refreshToken?: string; expiresIn?: number; requiresTwoFactor?: boolean; challengeToken?: string }
    | null;
  if (data?.requiresTwoFactor) {
    // First factor passed: keep the challenge server side in a short-lived HttpOnly cookie. The page only learns that a
    // code is needed, and completes the sign-in at /api/auth/two-factor.
    if (!data.challengeToken) return NextResponse.json({ error: "identity_error" }, { status: 502 });
    const challenge = NextResponse.json({ twoFactorRequired: true });
    setTwoFactorChallenge(challenge, data.challengeToken, data.expiresIn ?? 300, cfg.secureCookies);
    return challenge;
  }
  if (!data?.accessToken) return NextResponse.json({ error: "identity_error" }, { status: 502 });

  // Consent before anything else (ADR-0013): ask authservice whether the Terms/Privacy versions in force are accepted
  // and tell the page, which routes to the consent step instead of the app. Unknown counts as "required".
  const consent = await fetchConsentStatus(cfg, data.accessToken);
  const response = NextResponse.json({ authenticated: true, consentRequired: !consent || consent.required });
  establishSession(
    response,
    { accessToken: data.accessToken, refreshToken: data.refreshToken, expiresIn: data.expiresIn ?? 3600 },
    consent,
    cfg,
  );
  clearTwoFactorChallenge(response, cfg.secureCookies);
  return response;
}
