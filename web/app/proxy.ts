import { NextResponse, type NextRequest } from "next/server";
import { verifyAccessToken } from "@/lib/jwt";
import { authConfig } from "@/lib/runtime-config";
import { ACCESS_COOKIE, clearSession } from "@/lib/session";

// The edge gate (Next 16's `proxy.ts`, formerly `middleware.ts`). Not to be confused with the BFF
// catch-all at app/api/proxy. It protects pages: verified signature, issuer and audience, an explicit
// public-route list, `?redirect=` preserved. The APIs behind the BFF enforce their own authorization.

const PUBLIC_PATHS = new Set(["/", "/login", "/healthz", "/api/config", "/api/auth/login", "/api/auth/session"]);

export async function proxy(request: NextRequest) {
  const { pathname, search } = request.nextUrl;
  if (PUBLIC_PATHS.has(pathname)) return NextResponse.next();

  const cfg = authConfig();
  const payload = await verifyAccessToken(request.cookies.get(ACCESS_COOKIE)?.value, cfg);
  if (payload) return NextResponse.next();

  // Redirect relative to the public origin Next derived, not to request.url (internal host behind a platform proxy).
  const login = request.nextUrl.clone();
  login.pathname = "/login";
  login.search = `?redirect=${encodeURIComponent(pathname + search)}`;
  const response = pathname.startsWith("/api/")
    ? NextResponse.json({ error: "unauthenticated" }, { status: 401 })
    : NextResponse.redirect(login);
  clearSession(response, cfg.secureCookies);
  return response;
}

export const config = { matcher: ["/((?!_next/static|_next/image|favicon.ico).*)"] };
