import { NextResponse, type NextRequest } from "next/server";
import { verifyAccessToken } from "@/lib/jwt";
import { authConfig } from "@/lib/runtime-config";
import { ACCESS_COOKIE, CONSENT_COOKIE, REFRESHED_COOKIE, REFRESH_COOKIE, clearSession } from "@/lib/session";

// The edge gate (Next 16's `proxy.ts`, formerly `middleware.ts`). Not to be confused with the BFF
// catch-all at app/api/proxy. It protects pages: verified signature, issuer and audience, an explicit
// public-route list, `?redirect=` preserved. The APIs behind the BFF enforce their own authorization.
//
// Two more jobs, both UX rather than authorization (ADR-0013):
//  - a lapsed access token with a refresh token sends a NAVIGATION through /api/auth/refresh (which rotates the
//    token, single-flight, and comes back); API calls are refreshed by their own route handler.
//  - consent before anything else: a verified session without the consent marker may only reach the consent step,
//    the auth routes and the public pages. authservice holds the authoritative acceptance record.

const PUBLIC_PATHS = new Set([
  "/",
  "/login",
  "/healthz",
  "/api/config",
  "/api/auth/login",
  "/api/auth/session",
  "/api/auth/refresh",
  "/account-deleted",
]);

/** Reachable by a signed-in account that has not (yet) accepted the current versions. */
const CONSENT_EXEMPT = new Set(["/consent"]);
const isConsentExempt = (pathname: string) => CONSENT_EXEMPT.has(pathname) || pathname.startsWith("/api/auth/");

export async function proxy(request: NextRequest) {
  const { pathname, search } = request.nextUrl;
  if (PUBLIC_PATHS.has(pathname)) return NextResponse.next();

  const cfg = authConfig();
  const isApi = pathname.startsWith("/api/");
  const payload = await verifyAccessToken(request.cookies.get(ACCESS_COOKIE)?.value, cfg);

  if (payload) {
    if (!request.cookies.get(CONSENT_COOKIE)?.value && !isConsentExempt(pathname)) {
      if (isApi) return NextResponse.json({ error: "consent_required" }, { status: 403 });
      const consent = request.nextUrl.clone();
      consent.pathname = "/consent";
      consent.search = `?redirect=${encodeURIComponent(pathname + search)}`;
      return NextResponse.redirect(consent);
    }
    return NextResponse.next();
  }

  // The access cookie lapsed but the session may be alive: rotate through the refresh route, once per navigation.
  const hasRefresh = Boolean(request.cookies.get(REFRESH_COOKIE)?.value);
  if (hasRefresh && !request.cookies.get(REFRESHED_COOKIE)?.value) {
    // API paths pass through: the proxy route handler and the auth routes rotate for themselves.
    if (isApi) return NextResponse.next();
    const refresh = request.nextUrl.clone();
    refresh.pathname = "/api/auth/refresh";
    refresh.search = `?redirect=${encodeURIComponent(pathname + search)}`;
    return NextResponse.redirect(refresh);
  }

  // Redirect relative to the public origin Next derived, not to request.url (internal host behind a platform proxy).
  const login = request.nextUrl.clone();
  login.pathname = "/login";
  login.search = `?redirect=${encodeURIComponent(pathname + search)}`;
  const response = isApi ? NextResponse.json({ error: "unauthenticated" }, { status: 401 }) : NextResponse.redirect(login);
  clearSession(response, cfg.secureCookies);
  return response;
}

export const config = { matcher: ["/((?!_next/static|_next/image|favicon.ico).*)"] };
