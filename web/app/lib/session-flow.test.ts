import { NextResponse } from "next/server";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { resetRefreshState } from "./refresh";
import type { AuthConfig } from "./runtime-config";
import { ACCESS_COOKIE, CONSENT_COOKIE, REFRESH_COOKIE } from "./session";
import { establishSession, remainingSeconds, resolveAccessToken } from "./session-flow";

const cfg: AuthConfig = { url: "http://identity.invalid", issuer: "i", audience: "a", secureCookies: true };
const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status });
const consentOk = { terms: { requiredVersion: "t1" }, privacy: { requiredVersion: "p1" }, requiresConsent: false };

beforeEach(() => resetRefreshState());

describe("resolveAccessToken", () => {
  it("uses a verified access cookie as is and never touches the refresh token", async () => {
    const fetchSpy = vi.fn();

    const out = await resolveAccessToken({ access: "good", refresh: "r" }, cfg, { verify: async () => ({ sub: "s" }), fetch: fetchSpy as never });

    expect(out).toEqual({ kind: "ok", accessToken: "good" });
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it("rotates when the access cookie has lapsed and hands back the new pair with the consent status", async () => {
    const impl = vi.fn(async (url: string | URL | Request) =>
      String(url).endsWith("/refresh") ? json(200, { accessToken: "new", refreshToken: "r2", expiresIn: 600 }) : json(200, consentOk));

    const out = await resolveAccessToken({ refresh: "r1" }, cfg, { verify: async () => null, fetch: impl as never });

    expect(out).toMatchObject({ kind: "ok", accessToken: "new", rotated: { tokens: { refreshToken: "r2" }, consent: { required: false } } });
  });

  it("says the session is over when there is nothing to rotate, or the refresh token was refused", async () => {
    expect(await resolveAccessToken({}, cfg, { verify: async () => null })).toEqual({ kind: "none" });
    const refused = vi.fn(async () => json(401, {}));
    expect(await resolveAccessToken({ refresh: "r" }, cfg, { verify: async () => null, fetch: refused as never })).toEqual({ kind: "none" });
  });

  it("says unavailable, not over, when the identity service cannot be asked", async () => {
    const down = vi.fn(async () => {
      throw new TypeError("down");
    });

    expect(await resolveAccessToken({ refresh: "r" }, cfg, { verify: async () => null, fetch: down as never })).toEqual({ kind: "unavailable" });
  });
});

describe("establishSession", () => {
  const tokens = { accessToken: "a", refreshToken: "r", expiresIn: 600 };

  it("sets the consent marker only when authservice says the versions in force are accepted", () => {
    const res = NextResponse.json({});

    establishSession(res, tokens, { required: false, terms: "t1", privacy: "p1" }, cfg);

    expect(res.cookies.get(ACCESS_COOKIE)).toMatchObject({ value: "a", httpOnly: true, sameSite: "strict", secure: true });
    expect(res.cookies.get(REFRESH_COOKIE)).toMatchObject({ value: "r", httpOnly: true });
    expect(res.cookies.get(CONSENT_COOKIE)).toMatchObject({ value: "t1|p1", httpOnly: true, maxAge: 600 });
  });

  it.each([
    ["required", { required: true, terms: "t2", privacy: "p1" }],
    ["unknown (fails closed)", null],
  ])("withdraws the marker when consent is %s", (_label, consent) => {
    const res = NextResponse.json({});

    establishSession(res, tokens, consent, cfg);

    expect(res.cookies.get(CONSENT_COOKIE)).toMatchObject({ value: "", maxAge: 0 });
  });
});

describe("remainingSeconds", () => {
  const token = (exp: number) => `h.${Buffer.from(JSON.stringify({ exp })).toString("base64url")}.s`;

  it("is the time left on the token, at least one second", () => {
    expect(remainingSeconds(token(1_000_100), 1_000_000_000)).toBe(100);
    expect(remainingSeconds(token(5), 1_000_000_000)).toBe(1);
  });

  it("falls back to a minute for anything it cannot read", () => {
    expect(remainingSeconds("not-a-token")).toBe(60);
  });
});
