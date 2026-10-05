import { cookies, headers } from "next/headers";
import { NextResponse } from "next/server";
import { acceptCurrentConsents, fetchConsentStatus, localeFrom } from "@/lib/identity";
import { authConfig } from "@/lib/runtime-config";
import { ACCESS_COOKIE, REFRESH_COOKIE, clearSession, setConsentMarker } from "@/lib/session";
import { establishSession, remainingSeconds, resolveAccessToken } from "@/lib/session-flow";

export const dynamic = "force-dynamic";

const NO_STORE = { "Cache-Control": "no-store" };

async function session() {
  const jar = await cookies();
  const cfg = authConfig();
  const resolved = await resolveAccessToken({ access: jar.get(ACCESS_COOKIE)?.value, refresh: jar.get(REFRESH_COOKIE)?.value }, cfg);
  return { cfg, resolved };
}

/** What the consent step shows: whether acceptance is needed and which versions are in force. */
export async function GET() {
  const { cfg, resolved } = await session();
  if (resolved.kind === "unavailable") return NextResponse.json({ error: "identity_unavailable" }, { status: 503, headers: NO_STORE });
  if (resolved.kind === "none") {
    const response = NextResponse.json({ error: "unauthenticated" }, { status: 401, headers: NO_STORE });
    clearSession(response, cfg.secureCookies);
    return response;
  }
  const status = await fetchConsentStatus(cfg, resolved.accessToken);
  if (!status) return NextResponse.json({ error: "identity_unavailable" }, { status: 503, headers: NO_STORE });
  const response = NextResponse.json(status, { headers: NO_STORE });
  if (resolved.rotated) establishSession(response, resolved.rotated.tokens, status, cfg);
  else if (!status.required) setConsentMarker(response, status, remainingSeconds(resolved.accessToken), cfg.secureCookies);
  return response;
}

/**
 * Accepts the versions in force. The body only says "yes": which versions is decided by authservice at the moment of
 * the call, so a client cannot record acceptance of a version it was not shown by asking for another.
 */
export async function POST(request: Request) {
  const { cfg, resolved } = await session();
  if (resolved.kind === "unavailable") return NextResponse.json({ error: "identity_unavailable" }, { status: 503, headers: NO_STORE });
  if (resolved.kind === "none") return NextResponse.json({ error: "unauthenticated" }, { status: 401, headers: NO_STORE });

  const body = (await request.json().catch(() => null)) as { acceptTerms?: unknown; acceptPrivacy?: unknown } | null;
  if (body?.acceptTerms !== true || body?.acceptPrivacy !== true) {
    return NextResponse.json({ error: "both_required" }, { status: 400, headers: NO_STORE });
  }
  const status = await acceptCurrentConsents(cfg, resolved.accessToken, localeFrom((await headers()).get("accept-language")));
  if (!status) return NextResponse.json({ error: "identity_unavailable" }, { status: 502, headers: NO_STORE });

  const response = NextResponse.json(status, { headers: NO_STORE });
  if (resolved.rotated) establishSession(response, resolved.rotated.tokens, status, cfg);
  else setConsentMarker(response, status.required ? null : status, remainingSeconds(resolved.accessToken), cfg.secureCookies);
  return response;
}
