import { cookies } from "next/headers";
import { NextResponse, type NextRequest } from "next/server";
import { routeFor } from "@/lib/proxy-routing";
import { backendCandidates } from "@/lib/runtime-config";
import { ACCESS_COOKIE } from "@/lib/session";

export const dynamic = "force-dynamic";

// Sized to out-wait a scale-to-zero cold start on the callee (P7, FRONTEND-BFF §5).
const TIMEOUT_MS = 35_000;
const MAX_BODY_BYTES = 1_048_576; // records are small and untrusted: bound them at the edge
const PASS_HEADERS = ["content-type", "content-disposition", "content-length", "retry-after", "cache-control"];

async function handle(request: NextRequest, context: { params: Promise<{ path: string[] }> }) {
  const { path } = await context.params;
  const route = routeFor(path);
  if (!route) return NextResponse.json({ error: "not_found" }, { status: 404 });

  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  if (!token) return NextResponse.json({ error: "unauthenticated" }, { status: 401 });

  let body: ArrayBuffer | undefined;
  if (request.method !== "GET" && request.method !== "HEAD") {
    body = await request.arrayBuffer();
    if (body.byteLength > MAX_BODY_BYTES) return NextResponse.json({ error: "payload_too_large" }, { status: 413 });
  }

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
  return NextResponse.json({ error: "backend_unavailable" }, { status: 503 });
}

export { handle as GET, handle as POST, handle as PUT, handle as PATCH, handle as DELETE };
