import { NextResponse, type NextRequest } from "next/server";
import { verifyAccessToken } from "@/lib/jwt";
import { isSameOriginRequest, isStateChanging } from "@/lib/csrf";
import { authConfig } from "@/lib/runtime-config";
import { contentSecurityPolicy, generateNonce } from "@/lib/security-headers";
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

//
// Three more jobs on EVERY response that goes through here (ADR-0047, ADR-0048):
//  - a per-request nonce Content-Security-Policy, passed to Next on the request so it nonces its own bootstrap scripts;
//  - `Cache-Control: no-store` on everything except the two cacheable probes, because any page or API answer can carry
//    account data, a ticket or a receipt outcome;
//  - a same-origin check on every state-changing request (CSRF, in addition to SameSite=Strict cookies).

const PUBLIC_PATHS = new Set([
  "/",
  "/login",
  "/register",
  "/verify-email",
  "/connect",
  "/privacy",
  "/delete-submission",
  "/healthz",
  "/api/config",
  "/api/auth/login",
  "/api/auth/two-factor",
  "/api/auth/register",
  "/api/auth/verify-email",
  "/api/auth/resend-verification",
  "/api/auth/consent-versions",
  "/api/auth/session",
  "/api/auth/refresh",
  "/api/receipts",
  "/account-deleted",
]);

/** Probes and runtime config set their own short cache lifetime; everything else is never stored. */
const CACHEABLE = new Set(["/healthz", "/api/config"]);

/** The two Signals reads through the catch-all BFF route. Whether their answer is cacheable is decided by that route, per status. */
const isSignalsRead = (request: NextRequest) => {
  if (request.method !== "GET") return false;
  const path = request.nextUrl.pathname;
  return path === "/api/proxy/v1/signals/employers" || path.startsWith("/api/proxy/v1/signals/employers/");
};

/** Reachable by a signed-in account that has not (yet) accepted the current versions. */
const CONSENT_EXEMPT = new Set(["/consent"]);
const isConsentExempt = (pathname: string) => CONSENT_EXEMPT.has(pathname) || pathname.startsWith("/api/auth/");

export async function proxy(request: NextRequest) {
  const nonce = generateNonce();
  const csp = contentSecurityPolicy(nonce, process.env.NODE_ENV === "production");
  // Next reads the nonce from the CSP header of the REQUEST and stamps it on the scripts it renders.
  const forwarded = new Headers(request.headers);
  forwarded.set("content-security-policy", csp);
  forwarded.set("x-nonce", nonce);

  let passedThrough = false;
  const response = await gate(request, () => {
    passedThrough = true;
    return NextResponse.next({ request: { headers: forwarded } });
  });
  response.headers.set("Content-Security-Policy", csp);
  // A signals read that reached the BFF route sets its own (bounded, private) caching there; everything the gate itself answers,
  // a redirect or a 401, is still no-store (ADR-0068).
  const routeOwnsCaching = passedThrough && isSignalsRead(request);
  if (!CACHEABLE.has(request.nextUrl.pathname) && !routeOwnsCaching) response.headers.set("Cache-Control", "no-store");
  return response;
}

async function gate(request: NextRequest, pass: () => NextResponse): Promise<NextResponse> {
  const { pathname, search } = request.nextUrl;

  if (pathname.startsWith("/api/") && isStateChanging(request.method) && !isSameOriginRequest(request.headers)) {
    return NextResponse.json({ error: "cross_origin_request" }, { status: 403 });
  }
  if (PUBLIC_PATHS.has(pathname)) return pass();

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
    return pass();
  }

  // The access cookie lapsed but the session may be alive: rotate through the refresh route, once per navigation.
  const hasRefresh = Boolean(request.cookies.get(REFRESH_COOKIE)?.value);
  if (hasRefresh && !request.cookies.get(REFRESHED_COOKIE)?.value) {
    // API paths pass through: the proxy route handler and the auth routes rotate for themselves.
    if (isApi) return pass();
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
