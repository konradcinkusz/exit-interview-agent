// CSRF for state-changing BFF routes. The first line of defence is the cookies: SameSite=Strict (lib/session.ts), so a
// browser does not attach them to a request started from another site. This check is the second: a state-changing
// request that a browser marks as cross-site, or whose Origin is not this host, is refused before any handler runs
// (proxy.ts). Requests with neither header (curl, server-to-server) are not a CSRF vector: they cannot borrow a
// browser's cookies. The anonymous receipt deletion needs the check too, even though it has no cookie, because a hostile
// page could otherwise make a victim's browser spend the per-client rate limit.

const SAFE_METHODS = new Set(["GET", "HEAD", "OPTIONS"]);

export function isStateChanging(method: string): boolean {
  return !SAFE_METHODS.has(method.toUpperCase());
}

/** True when the request is allowed to change state: same origin as far as the browser's own headers can tell. */
export function isSameOriginRequest(headers: Headers): boolean {
  const site = headers.get("sec-fetch-site");
  if (site && site !== "same-origin" && site !== "none") return false;

  const origin = headers.get("origin");
  if (origin !== null) {
    // "null" (a sandboxed or opaque origin) is never ours.
    let originHost: string;
    try {
      originHost = new URL(origin).host;
    } catch {
      return false;
    }
    const host = headers.get("x-forwarded-host")?.split(",")[0]?.trim() || headers.get("host");
    return Boolean(host) && originHost === host;
  }
  return true;
}
