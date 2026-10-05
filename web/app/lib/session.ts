import type { NextResponse } from "next/server";

// Sessions (FRONTEND-BFF §3): tokens live in HttpOnly cookies set and cleared by BFF routes only.
// Client JavaScript never sees a token. Set and delete share ONE attribute function, because a
// delete with mismatched attributes silently leaves the cookie alive (login loop after logout).

export const ACCESS_COOKIE = "eia_access";
export const REFRESH_COOKIE = "eia_refresh";

export function cookieAttributes(secure: boolean, maxAge?: number) {
  return { httpOnly: true, secure, sameSite: "strict" as const, path: "/", ...(maxAge === undefined ? {} : { maxAge }) };
}

export interface Tokens {
  accessToken: string;
  refreshToken?: string;
  /** Access-token lifetime in seconds. */
  expiresIn: number;
}

export function setSession(res: NextResponse, tokens: Tokens, secure: boolean): void {
  res.cookies.set(ACCESS_COOKIE, tokens.accessToken, cookieAttributes(secure, tokens.expiresIn));
  if (tokens.refreshToken) {
    res.cookies.set(REFRESH_COOKIE, tokens.refreshToken, cookieAttributes(secure, 7 * 24 * 3600));
  }
}

export function clearSession(res: NextResponse, secure: boolean): void {
  for (const name of [ACCESS_COOKIE, REFRESH_COOKIE]) {
    res.cookies.set(name, "", cookieAttributes(secure, 0));
  }
}
