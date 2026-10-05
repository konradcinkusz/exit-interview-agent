import type { BackendName } from "./runtime-config";

export interface Route {
  backend: BackendName;
  /** Path on the backend, without query string. */
  upstreamPath: string;
}

/**
 * Prefix -> backend routing for the catch-all proxy (FRONTEND-BFF §5), so the browser has exactly one
 * base URL: `/api/proxy/v1/...` is the interview-service's `/api/v1/...`. Only versioned API paths are
 * reachable: health, OpenAPI and anything else on the backend are not exposed through the BFF.
 */
export function routeFor(segments: string[]): Route | null {
  if (segments.length < 2 || segments[0] !== "v1") return null;
  if (segments.some((s) => s === "" || s === "." || s === ".." || s.includes("\\") || s.includes("%"))) return null;
  return { backend: "interview-service", upstreamPath: `/api/${segments.join("/")}` };
}
