import { cookies } from "next/headers";
import { NextResponse } from "next/server";
import { verifyAccessToken } from "@/lib/jwt";
import { revokeSessions } from "@/lib/identity";
import { authConfig } from "@/lib/runtime-config";
import { ACCESS_COOKIE, CONSENT_COOKIE, REFRESH_COOKIE, clearSession } from "@/lib/session";
import { establishSession, resolveAccessToken } from "@/lib/session-flow";

export const dynamic = "force-dynamic";

/**
 * Rehydrates client state on page load: client JS cannot read the HttpOnly cookie, by design. An expired access
 * token with a live refresh token is rotated here, so a returning visitor is still signed in.
 */
export async function GET() {
  const jar = await cookies();
  const cfg = authConfig();
  const resolved = await resolveAccessToken({ access: jar.get(ACCESS_COOKIE)?.value, refresh: jar.get(REFRESH_COOKIE)?.value }, cfg);
  if (resolved.kind === "unavailable") {
    return NextResponse.json({ error: "identity_unavailable" }, { status: 503, headers: { "Cache-Control": "no-store" } });
  }
  if (resolved.kind === "none") {
    const response = NextResponse.json({ authenticated: false }, { headers: { "Cache-Control": "no-store" } });
    if (jar.get(ACCESS_COOKIE) || jar.get(REFRESH_COOKIE)) clearSession(response, cfg.secureCookies);
    return response;
  }
  const payload = await verifyAccessToken(resolved.accessToken, cfg);
  const consentRequired = resolved.rotated ? !resolved.rotated.consent || resolved.rotated.consent.required : !jar.get(CONSENT_COOKIE)?.value;
  const response = NextResponse.json(
    payload ? { authenticated: true, subject: payload.sub, consentRequired } : { authenticated: false },
    { headers: { "Cache-Control": "no-store" } },
  );
  if (resolved.rotated) establishSession(response, resolved.rotated.tokens, resolved.rotated.consent, cfg);
  return response;
}

/**
 * Logout: revokes the account's refresh tokens at authservice (best effort, so a stolen cookie cannot outlive the
 * click), then deletes the cookies with the same attributes they were set with.
 */
export async function DELETE() {
  const jar = await cookies();
  const cfg = authConfig();
  const resolved = await resolveAccessToken({ access: jar.get(ACCESS_COOKIE)?.value, refresh: jar.get(REFRESH_COOKIE)?.value }, cfg);
  if (resolved.kind === "ok") await revokeSessions(cfg, resolved.accessToken);
  const response = NextResponse.json({ authenticated: false });
  clearSession(response, cfg.secureCookies);
  return response;
}
