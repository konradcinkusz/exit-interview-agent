import { cookies } from "next/headers";
import { NextResponse, type NextRequest } from "next/server";
import { routeFor } from "@/lib/proxy-routing";
import { refreshSession } from "@/lib/refresh";
import { authConfig, backendCandidates } from "@/lib/runtime-config";
import { ACCESS_COOKIE, CONSENT_COOKIE, REFRESH_COOKIE, clearSession } from "@/lib/session";
import { establishSession, resolveAccessToken } from "@/lib/session-flow";

export const dynamic = "force-dynamic";

// Sized to out-wait a scale-to-zero cold start on the callee (P7, FRONTEND-BFF §5).
const TIMEOUT_MS = 35_000;
const MAX_BODY_BYTES = 1_048_576; // records are small and untrusted: bound them at the edge
const PASS_HEADERS = ["content-type", "content-disposition", "content-length", "retry-after", "cache-control"];

type Upstream = NextResponse | "backend_unavailable";

async function callBackend(
  request: NextRequest,
  route: { backend: Parameters<typeof backendCandidates>[0]; upstreamPath: string },
  token: string,
  body: ArrayBuffer | undefined,
): Promise<Upstream> {
  const headers = new Headers({ authorization: `Bearer ${token}`, accept: request.headers.get("accept") ?? "application/json" });
  const contentType = request.headers.get("content-type");
  if (contentType) headers.set("content-type", contentType);

  for (const base of backendCandidates(route.backend)) {
    try {
      const upstream = await fetch(`${base}${route.upstreamPath}${request.nextUrl.search}`, {
        method: request.method,
        headers,
        body,
        redirect: "manual", // a redirect between services is always a configuration bug
        signal: AbortSignal.timeout(TIMEOUT_MS),
      });
      if (upstream.status >= 300 && upstream.status < 400) {
        console.error(`proxy: ${base} answered ${upstream.status} (redirect between services)`);
        return NextResponse.json({ error: "bad_gateway" }, { status: 502 });
      }
      if (upstream.status === 403) continue; // wrong ingress for this rung: try the next candidate
      const out = new Headers();
      for (const name of PASS_HEADERS) {
        const value = upstream.headers.get(name);
        if (value) out.set(name, value);
      }
      return new NextResponse(upstream.body, { status: upstream.status, headers: out });
    } catch (error) {
      if (error instanceof DOMException && error.name === "TimeoutError") {
        return NextResponse.json({ error: "gateway_timeout" }, { status: 504 });
      }
      // connection refused / DNS failure: not a verdict, try the next candidate
    }
  }
  return "backend_unavailable";
}

async function handle(request: NextRequest, context: { params: Promise<{ path: string[] }> }) {
  const { path } = await context.params;
  const route = routeFor(path);
  if (!route) return NextResponse.json({ error: "not_found" }, { status: 404 });

  const cfg = authConfig();
  const jar = await cookies();
  const refreshCookie = jar.get(REFRESH_COOKIE)?.value;
  const resolved = await resolveAccessToken({ access: jar.get(ACCESS_COOKIE)?.value, refresh: refreshCookie }, cfg);
  if (resolved.kind === "unavailable") return NextResponse.json({ error: "identity_unavailable" }, { status: 503 });
  if (resolved.kind === "none") {
    const response = NextResponse.json({ error: "unauthenticated" }, { status: 401 });
    if (jar.get(ACCESS_COOKIE) || refreshCookie) clearSession(response, cfg.secureCookies);
    return response;
  }

  // Consent before anything else: no API call is made for an account that has not accepted the versions in force.
  const consentOk = resolved.rotated ? Boolean(resolved.rotated.consent && !resolved.rotated.consent.required) : Boolean(jar.get(CONSENT_COOKIE)?.value);
  if (!consentOk) return NextResponse.json({ error: "consent_required" }, { status: 403 });

  let body: ArrayBuffer | undefined;
  if (request.method !== "GET" && request.method !== "HEAD") {
    body = await request.arrayBuffer();
    if (body.byteLength > MAX_BODY_BYTES) return NextResponse.json({ error: "payload_too_large" }, { status: 413 });
  }

  let rotated = resolved.rotated;
  let result = await callBackend(request, route, resolved.accessToken, body);

  // The cookie verified here but the service refused it (a token the service no longer honours): rotate once and retry.
  if (result !== "backend_unavailable" && result.status === 401 && !rotated && refreshCookie) {
    const next = await refreshSession(cfg, refreshCookie);
    if (next.ok) {
      rotated = { tokens: next.session.tokens, consent: next.session.consent };
      result = await callBackend(request, route, next.session.tokens.accessToken, body);
    }
  }

  if (result === "backend_unavailable") return NextResponse.json({ error: "backend_unavailable" }, { status: 503 });
  if (rotated) establishSession(result, rotated.tokens, rotated.consent, cfg);
  return result;
}

export { handle as GET, handle as POST, handle as PUT, handle as PATCH, handle as DELETE };
