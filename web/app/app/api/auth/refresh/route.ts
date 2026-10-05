import { cookies } from "next/headers";
import { NextResponse, type NextRequest } from "next/server";
import { authConfig, identityConfigured } from "@/lib/runtime-config";
import { refreshSession } from "@/lib/refresh";
import { safeRedirect } from "@/lib/safe-redirect";
import { REFRESH_COOKIE, clearSession, setRefreshedFlag } from "@/lib/session";
import { establishSession } from "@/lib/session-flow";

export const dynamic = "force-dynamic";

/**
 * Rotates the refresh token (single-flight). Two forms, one implementation:
 *  - GET ?redirect=: the edge gate sends a navigation here when the access cookie has lapsed; on success the browser
 *    is sent back to where it was going, on failure to the sign-in page.
 *  - POST: for client code that wants to refresh explicitly; answers JSON.
 */
async function rotate(request: NextRequest, mode: "redirect" | "json") {
  const cfg = authConfig();
  const refresh = (await cookies()).get(REFRESH_COOKIE)?.value;
  const target = safeRedirect(request.nextUrl.searchParams.get("redirect"));

  const over = () => {
    let response: NextResponse;
    if (mode === "redirect") {
      const login = request.nextUrl.clone();
      login.pathname = "/login";
      login.search = `?redirect=${encodeURIComponent(target)}`;
      response = NextResponse.redirect(login);
    } else {
      response = NextResponse.json({ error: "unauthenticated" }, { status: 401 });
    }
    clearSession(response, cfg.secureCookies);
    return response;
  };

  if (!identityConfigured(cfg) || !refresh) return over();
  const result = await refreshSession(cfg, refresh);
  if (!result.ok) {
    return result.reason === "unavailable" ? NextResponse.json({ error: "identity_unavailable" }, { status: 503 }) : over();
  }

  const response = mode === "redirect" ? NextResponse.redirect(new URL(target, request.nextUrl)) : NextResponse.json({ refreshed: true });
  establishSession(response, result.session.tokens, result.session.consent, cfg);
  setRefreshedFlag(response, cfg.secureCookies);
  return response;
}

export const GET = (request: NextRequest) => rotate(request, "redirect");
export const POST = (request: NextRequest) => rotate(request, "json");
