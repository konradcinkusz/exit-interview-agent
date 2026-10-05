import { NextRequest } from "next/server";
import { beforeEach, describe, expect, it, vi } from "vitest";

const verify = vi.fn();
vi.mock("@/lib/jwt", () => ({ verifyAccessToken: (...args: unknown[]) => verify(...args) }));

const { proxy } = await import("./proxy");

const request = (path: string, cookies: Record<string, string> = {}, init: { method?: string; headers?: Record<string, string> } = {}) =>
  new NextRequest(`http://localhost:3000${path}`, {
    method: init.method,
    headers: { cookie: Object.entries(cookies).map(([k, v]) => `${k}=${v}`).join("; "), ...init.headers },
  });

const location = (res: Response) => new URL(res.headers.get("location") ?? "http://none").pathname + new URL(res.headers.get("location") ?? "http://none").search;

beforeEach(() => {
  verify.mockReset();
  process.env.AUTH_SERVICE_URL = "http://identity.invalid";
  process.env.AUTH_ISSUER = "i";
  process.env.AUTH_AUDIENCE = "a";
});

describe("edge gate: consent before anything else", () => {
  it("lets a verified session with the consent marker through", async () => {
    verify.mockResolvedValue({ sub: "s" });

    const res = await proxy(request("/account", { eia_access: "t", eia_consent: "t1|p1" }));

    expect(res.headers.get("x-middleware-next")).toBe("1");
  });

  it("sends a verified session WITHOUT the marker to the consent step, preserving where it was going", async () => {
    verify.mockResolvedValue({ sub: "s" });

    const res = await proxy(request("/account?tab=1", { eia_access: "t" }));

    expect(res.status).toBe(307);
    expect(location(res)).toBe(`/consent?redirect=${encodeURIComponent("/account?tab=1")}`);
  });

  it("answers API calls without the marker with 403 consent_required, not a redirect", async () => {
    verify.mockResolvedValue({ sub: "s" });

    const res = await proxy(request("/api/proxy/v1/me", { eia_access: "t" }));

    expect(res.status).toBe(403);
    expect(await res.json()).toEqual({ error: "consent_required" });
  });

  it("still allows the consent step and the auth routes without the marker", async () => {
    verify.mockResolvedValue({ sub: "s" });

    for (const path of ["/consent", "/api/auth/consent", "/api/auth/account"]) {
      expect((await proxy(request(path, { eia_access: "t" }))).headers.get("x-middleware-next")).toBe("1");
    }
  });

  it("keeps public pages public", async () => {
    for (const path of [
      "/", "/login", "/register", "/verify-email", "/connect", "/privacy", "/delete-submission", "/account-deleted",
      "/api/auth/refresh", "/api/auth/two-factor", "/api/auth/register", "/api/receipts",
    ]) {
      expect((await proxy(request(path))).headers.get("x-middleware-next")).toBe("1");
    }
  });
});

describe("edge gate: refresh", () => {
  it("routes a navigation with a lapsed access token and a refresh token through the refresh route", async () => {
    verify.mockResolvedValue(null);

    const res = await proxy(request("/account", { eia_refresh: "r" }));

    expect(location(res)).toBe(`/api/auth/refresh?redirect=${encodeURIComponent("/account")}`);
  });

  it("does not loop: right after a refresh that did not stick, it goes to sign-in", async () => {
    verify.mockResolvedValue(null);

    const res = await proxy(request("/account", { eia_refresh: "r", eia_refreshed: "1" }));

    expect(location(res)).toBe(`/login?redirect=${encodeURIComponent("/account")}`);
  });

  it("lets API paths through so their own handler rotates, and answers 401 when there is nothing to rotate", async () => {
    verify.mockResolvedValue(null);

    expect((await proxy(request("/api/proxy/v1/me", { eia_refresh: "r" }))).headers.get("x-middleware-next")).toBe("1");
    expect((await proxy(request("/api/proxy/v1/me"))).status).toBe(401);
  });

  it("sends a visitor with no session to sign-in and clears stale cookies", async () => {
    verify.mockResolvedValue(null);

    const res = await proxy(request("/account"));

    expect(location(res)).toBe(`/login?redirect=${encodeURIComponent("/account")}`);
  });
});

describe("edge gate: protected product pages", () => {
  it("sends a visitor with no session away from the pages that need one", async () => {
    verify.mockResolvedValue(null);

    for (const path of ["/account", "/cli", "/api/auth/export", "/api/proxy/v1/tickets"]) {
      const res = await proxy(request(path));
      expect(res.status, path).toBeOneOf([307, 401]);
      expect(res.headers.get("x-middleware-next"), path).toBeNull();
    }
  });
});

describe("edge gate: nonce CSP and no-store on every response", () => {
  const nonceOf = (res: Response) => /'nonce-([^']+)'/.exec(res.headers.get("content-security-policy") ?? "")?.[1];

  it("sets a fresh nonce CSP, with no unsafe-inline for scripts, on public and gated answers alike", async () => {
    verify.mockResolvedValue({ sub: "s" });
    const responses = [
      await proxy(request("/")),
      await proxy(request("/")),
      await proxy(request("/account", { eia_access: "t", eia_consent: "t1|p1" })),
      await proxy(request("/account?x=1", { eia_access: "t" })), // a redirect to the consent step
    ];

    const nonces = responses.map(nonceOf);
    expect(nonces.every((n) => n && n.length >= 22)).toBe(true);
    expect(new Set(nonces).size).toBe(responses.length);
    for (const res of responses) expect(res.headers.get("content-security-policy")).not.toMatch(/script-src[^;]*unsafe-inline/);
  });

  it("hands the same nonce to Next on the forwarded request, which is how Next stamps its own scripts", async () => {
    const res = await proxy(request("/"));

    const forwarded = res.headers.get("x-middleware-request-content-security-policy") ?? "";
    expect(forwarded).toContain(`'nonce-${nonceOf(res)}'`);
    expect(res.headers.get("x-middleware-request-x-nonce")).toBe(nonceOf(res));
  });

  it("marks everything no-store except the two probes that set their own lifetime", async () => {
    verify.mockResolvedValue({ sub: "s" });

    for (const path of ["/", "/login", "/connect", "/delete-submission", "/api/receipts", "/api/auth/session"]) {
      expect((await proxy(request(path))).headers.get("cache-control"), path).toBe("no-store");
    }
    for (const path of ["/healthz", "/api/config"]) {
      expect((await proxy(request(path))).headers.get("cache-control"), path).toBeNull();
    }
    expect((await proxy(request("/account"))).headers.get("cache-control")).toBe("no-store"); // the sign-in redirect too
  });
});

describe("edge gate: CSRF on state-changing API routes", () => {
  const post = (path: string, headers: Record<string, string>, method = "POST") => proxy(request(path, {}, { method, headers: { host: "localhost:3000", ...headers } }));

  it("refuses a state-changing request whose Origin or Sec-Fetch-Site says another site", async () => {
    for (const path of ["/api/auth/login", "/api/receipts", "/api/auth/account", "/api/proxy/v1/tickets"]) {
      const foreign = await post(path, { origin: "https://evil.example.test" }, "DELETE");
      expect(foreign.status, path).toBe(403);
      expect(await foreign.json()).toEqual({ error: "cross_origin_request" });
      expect((await post(path, { "sec-fetch-site": "cross-site" })).status, path).toBe(403);
    }
  });

  it("lets a same-origin request and a request with no browser headers through to the next handler", async () => {
    expect((await post("/api/auth/login", { origin: "http://localhost:3000" })).headers.get("x-middleware-next")).toBe("1");
    expect((await post("/api/receipts", {}, "DELETE")).headers.get("x-middleware-next")).toBe("1");
  });

  it("never blocks a safe method, whatever it claims", async () => {
    expect((await proxy(request("/api/config", {}, { headers: { origin: "https://evil.example.test" } }))).headers.get("x-middleware-next")).toBe("1");
  });

  it("checks before the session, so a forged cross-site call learns nothing about whether it was signed in", async () => {
    verify.mockResolvedValue({ sub: "s" });

    const res = await post("/api/proxy/v1/tickets", { origin: "https://evil.example.test" });

    expect(res.status).toBe(403);
    expect(verify).not.toHaveBeenCalled();
  });
});

describe("caching of the Signals reads (ADR-0068)", () => {
  const signed = { eia_access: "t", eia_consent: "t1|p1" };

  it("leaves the caching of a signals read to the BFF route", async () => {
    verify.mockResolvedValue({ sub: "s" });
    for (const path of ["/api/proxy/v1/signals/employers?page=2", "/api/proxy/v1/signals/employers/demo-acme"]) {
      const res = await proxy(request(path, signed));
      expect(res.headers.get("x-middleware-next")).toBe("1");
      expect(res.headers.get("cache-control"), path).toBeNull();
    }
  });

  it.each([
    ["a POST to the same path", "/api/proxy/v1/signals/employers", "POST"],
    ["another API path", "/api/proxy/v1/me", "GET"],
    ["another signals path", "/api/proxy/v1/signals/other", "GET"],
    ["a page", "/signals", "GET"],
    ["the employer page", "/signals/demo-acme", "GET"],
  ])("stays no-store for %s", async (_name, path, method) => {
    verify.mockResolvedValue({ sub: "s" });
    const headers: Record<string, string> = method === "POST" ? { origin: "http://localhost:3000" } : {};
    const res = await proxy(request(path, signed, { method, headers }));
    expect(res.headers.get("cache-control")).toBe("no-store");
  });

  it("answers the edge gate's own refusals no-store, even for a signals read", async () => {
    verify.mockResolvedValue(null);
    const res = await proxy(request("/api/proxy/v1/signals/employers"));
    expect(res.status).toBe(401);
    expect(res.headers.get("cache-control")).toBe("no-store");
  });

  it("answers a signals read without the consent marker 403 and no-store", async () => {
    verify.mockResolvedValue({ sub: "s" });
    const res = await proxy(request("/api/proxy/v1/signals/employers", { eia_access: "t" }));
    expect(res.status).toBe(403);
    expect(res.headers.get("cache-control")).toBe("no-store");
  });

  it("gates the pages: signed out, /signals and an employer page go to sign-in and back", async () => {
    verify.mockResolvedValue(null);
    for (const path of ["/signals", "/signals/demo-acme"]) {
      const res = await proxy(request(path));
      expect(res.status).toBe(307);
      expect(location(res)).toBe(`/login?redirect=${encodeURIComponent(path)}`);
    }
  });
});
