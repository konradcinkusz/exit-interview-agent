import { NextResponse } from "next/server";
import { describe, expect, it } from "vitest";
import { ACCESS_COOKIE, REFRESH_COOKIE, clearSession, cookieAttributes, setSession } from "./session";

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
