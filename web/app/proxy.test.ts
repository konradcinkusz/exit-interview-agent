import { NextRequest } from "next/server";
import { beforeEach, describe, expect, it, vi } from "vitest";

const verify = vi.fn();
vi.mock("@/lib/jwt", () => ({ verifyAccessToken: (...args: unknown[]) => verify(...args) }));

const { proxy } = await import("./proxy");

const request = (path: string, cookies: Record<string, string> = {}) =>
  new NextRequest(`http://localhost:3000${path}`, {
    headers: { cookie: Object.entries(cookies).map(([k, v]) => `${k}=${v}`).join("; ") },
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
    for (const path of ["/", "/login", "/account-deleted", "/api/auth/refresh"]) {
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
