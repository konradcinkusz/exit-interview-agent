import { NextResponse } from "next/server";
import { backendCandidates, type BackendName } from "./runtime-config";

// One call to a backend through the candidate ladder (FRONTEND-BFF §5), shared by the authenticated catch-all proxy and
// the anonymous receipt route. It forwards ONLY the headers the caller passes: nothing is copied from the browser request
// implicitly, which is what keeps a secret header route-specific (ADR-0049).

// Sized to out-wait a scale-to-zero cold start on the callee (P7).
const TIMEOUT_MS = 35_000;
/** Response headers passed back to the browser. `cache-control` is deliberately absent: every BFF answer is no-store. */
const PASS_HEADERS = ["content-type", "content-disposition", "content-length", "retry-after"];

export const NO_STORE = { "Cache-Control": "no-store" } as const;

export type Upstream = NextResponse | "backend_unavailable";

export interface UpstreamCall {
  backend: BackendName;
  /** Path on the backend, without query string. */
  upstreamPath: string;
  search?: string;
  method: string;
  headers: Headers;
  body?: ArrayBuffer;
}

export async function callBackend(call: UpstreamCall): Promise<Upstream> {
  for (const base of backendCandidates(call.backend)) {
    try {
      const upstream = await fetch(`${base}${call.upstreamPath}${call.search ?? ""}`, {
        method: call.method,
        headers: call.headers,
        body: call.body,
        redirect: "manual", // a redirect between services is always a configuration bug
        signal: AbortSignal.timeout(TIMEOUT_MS),
      });
      if (upstream.status >= 300 && upstream.status < 400) {
        console.error(`proxy: ${base} answered ${upstream.status} (redirect between services)`);
        return NextResponse.json({ error: "bad_gateway" }, { status: 502, headers: NO_STORE });
      }
      if (upstream.status === 403) continue; // wrong ingress for this rung: try the next candidate
      const out = new Headers(NO_STORE);
      for (const name of PASS_HEADERS) {
        const value = upstream.headers.get(name);
        if (value) out.set(name, value);
      }
      // 204 and 304 must not carry a body, and a null body must stay null.
      return new NextResponse(upstream.status === 204 ? null : upstream.body, { status: upstream.status, headers: out });
    } catch (error) {
      if (error instanceof DOMException && error.name === "TimeoutError") {
        return NextResponse.json({ error: "gateway_timeout" }, { status: 504, headers: NO_STORE });
      }
      // connection refused / DNS failure: not a verdict, try the next candidate
    }
  }
  return "backend_unavailable";
}
