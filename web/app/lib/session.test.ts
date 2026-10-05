import { NextResponse } from "next/server";
import { describe, expect, it } from "vitest";
import { ACCESS_COOKIE, REFRESH_COOKIE, TWO_FACTOR_COOKIE, clearSession, clearTwoFactorChallenge, cookieAttributes, setSession, setTwoFactorChallenge } from "./session";

describe("session cookies", () => {
  it("sets HttpOnly, SameSite=Strict cookies and the secure flag follows configuration", () => {
    const res = NextResponse.json({});
    setSession(res, { accessToken: "a", refreshToken: "r", expiresIn: 60 }, true);

    const access = res.cookies.get(ACCESS_COOKIE);
    expect(access).toMatchObject({ value: "a", httpOnly: true, secure: true, sameSite: "strict", path: "/", maxAge: 60 });
    expect(res.cookies.get(REFRESH_COOKIE)?.httpOnly).toBe(true);
  });

  it("clears with the same attributes it set with, so the delete actually matches", () => {
    const res = NextResponse.json({});
    clearSession(res, false);

    for (const name of [ACCESS_COOKIE, REFRESH_COOKIE]) {
      expect(res.cookies.get(name)).toMatchObject({ value: "", ...cookieAttributes(false, 0) });
    }
  });
});

describe("two-factor challenge cookie", () => {
  it("is HttpOnly, SameSite=Strict and lives at most the 5 minutes authservice gives the challenge", () => {
    const res = NextResponse.json({});
    setTwoFactorChallenge(res, "challenge", 3600, true);

    expect(res.cookies.get(TWO_FACTOR_COOKIE)).toMatchObject({ value: "challenge", httpOnly: true, secure: true, sameSite: "strict", maxAge: 300 });
  });

  it("is cleared with the attributes it was set with, and by clearSession too", () => {
    const res = NextResponse.json({});
    clearTwoFactorChallenge(res, false);
    expect(res.cookies.get(TWO_FACTOR_COOKIE)).toMatchObject({ value: "", ...cookieAttributes(false, 0) });

    const all = NextResponse.json({});
    clearSession(all, false);
    expect(all.cookies.get(TWO_FACTOR_COOKIE)?.value).toBe("");
  });
});
