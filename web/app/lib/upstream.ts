import { NextResponse } from "next/server";
import { backendCandidates, type BackendName } from "./runtime-config";

// One call to a backend through the candidate ladder (FRONTEND-BFF §5), shared by the authenticated catch-all proxy and
// the anonymous receipt route. It forwards ONLY the headers the caller passes: nothing is copied from the browser request
// implicitly, which is what keeps a secret header route-specific (ADR-0049).

// Sized to out-wait a scale-to-zero cold start on the callee (P7).
const TIMEOUT_MS = 35_000;
/** Response headers passed back to the browser. `cache-control` is deliberately absent: every BFF answer is no-store, except the signals reads below. */
const PASS_HEADERS = ["content-type", "content-disposition", "content-length", "retry-after"];
/** Additionally passed for a signals read, so the browser can revalidate: the validators of the published snapshot (ADR-0068). */
const VALIDATOR_HEADERS = ["etag", "last-modified"];
/** Only these statuses of a signals read may carry caching: the figures, "not modified" and the uniform "nothing to show". */
const CACHEABLE_STATUSES = new Set([200, 304, 404]);
/** The longest the BFF lets a browser keep a signals answer: the longest publication interval (7 days). */
const MAX_AGE_CEILING = 7 * 24 * 3600;

export const NO_STORE = { "Cache-Control": "no-store" } as const;

/**
 * The caching a signals answer may carry through the BFF: `private, max-age=N` exactly as the service sent it (N is the time to
 * the next batch), clamped to the longest publication interval. Anything else (`public`, a missing header, a shape this
 * function does not know) becomes `no-store`: the BFF never makes a response `public` and never invents a lifetime.
 */
export function signalsCacheControl(upstream: string | null): string {
  const match = /^private, max-age=(\d{1,10})$/.exec(upstream ?? "");
  if (!match) return NO_STORE["Cache-Control"];
  return `private, max-age=${Math.min(Number(match[1]), MAX_AGE_CEILING)}`;
}

export type Upstream = NextResponse | "backend_unavailable";

export interface UpstreamCall {
  backend: BackendName;
  /** Path on the backend, without query string. */
  upstreamPath: string;
  search?: string;
  method: string;
  headers: Headers;
  body?: ArrayBuffer;
  /** A signals read: lets the validators and a bounded private lifetime through (ADR-0068). */
  signalsRead?: boolean;
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
      // 304 is not a redirect: it is the answer to a conditional request (a signals read revalidated with its ETag).
      if (upstream.status >= 300 && upstream.status < 400 && upstream.status !== 304) {
        console.error(`proxy: ${base} answered ${upstream.status} (redirect between services)`);
        return NextResponse.json({ error: "bad_gateway" }, { status: 502, headers: NO_STORE });
      }
      if (upstream.status === 403) continue; // wrong ingress for this rung: try the next candidate
      const out = new Headers(NO_STORE);
      for (const name of PASS_HEADERS) {
        const value = upstream.headers.get(name);
        if (value) out.set(name, value);
      }
      if (call.signalsRead && CACHEABLE_STATUSES.has(upstream.status)) {
        out.set("Cache-Control", signalsCacheControl(upstream.headers.get("cache-control")));
        // The answer depends on the session, which is a cookie at this origin (the bearer is injected here).
        out.set("Vary", "Cookie");
        for (const name of VALIDATOR_HEADERS) {
          const value = upstream.headers.get(name);
          if (value) out.set(name, value);
        }
      }
      // 204 and 304 must not carry a body.
      const bodiless = upstream.status === 204 || upstream.status === 304;
      return new NextResponse(bodiless ? null : upstream.body, { status: upstream.status, headers: out });
    } catch (error) {
      if (error instanceof DOMException && error.name === "TimeoutError") {
        return NextResponse.json({ error: "gateway_timeout" }, { status: 504, headers: NO_STORE });
      }
      // connection refused / DNS failure: not a verdict, try the next candidate
    }
  }
  return "backend_unavailable";
}
