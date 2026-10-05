import type { NextResponse } from "next/server";

// Sessions (FRONTEND-BFF §3): tokens live in HttpOnly cookies set and cleared by BFF routes only.
// Client JavaScript never sees a token. Set and delete share ONE attribute function, because a
// delete with mismatched attributes silently leaves the cookie alive (login loop after logout).

export const ACCESS_COOKIE = "eia_access";
export const REFRESH_COOKIE = "eia_refresh";
/**
 * Marker that the account has accepted the Terms and Privacy versions in force. A UX gate only: authservice holds the
 * authoritative acceptance record. It lives exactly as long as the access token, so it is re-derived at every
 * rotation and a bumped version reaches a signed-in user within one access-token lifetime (ADR-0013).
 */
export const CONSENT_COOKIE = "eia_consent";
/** Set for a few seconds after a redirect-style refresh so a refresh that cannot stick never loops. */
export const REFRESHED_COOKIE = "eia_refreshed";

/**
 * The second-factor challenge between the password step and the code step. authservice issues it with a 5-minute life; it
 * is useless for anything but completing that one sign-in, and it stays in this cookie so page JavaScript never holds it.
 */
export const TWO_FACTOR_COOKIE = "eia_2fa";

const ALL_COOKIES = [ACCESS_COOKIE, REFRESH_COOKIE, CONSENT_COOKIE, REFRESHED_COOKIE, TWO_FACTOR_COOKIE];
const REFRESH_COOKIE_SECONDS = 7 * 24 * 3600;

export function cookieAttributes(secure: boolean, maxAge?: number) {
  return { httpOnly: true, secure, sameSite: "strict" as const, path: "/", ...(maxAge === undefined ? {} : { maxAge }) };
}

export interface Tokens {
  accessToken: string;
  refreshToken?: string;
  /** Access-token lifetime in seconds. */
  expiresIn: number;
}

export interface ConsentMarker {
  terms: string;
  privacy: string;
}

export function setSession(res: NextResponse, tokens: Tokens, secure: boolean): void {
  res.cookies.set(ACCESS_COOKIE, tokens.accessToken, cookieAttributes(secure, tokens.expiresIn));
  if (tokens.refreshToken) {
    res.cookies.set(REFRESH_COOKIE, tokens.refreshToken, cookieAttributes(secure, REFRESH_COOKIE_SECONDS));
  }
}

/** Records (or, with null, withdraws) the consent marker; its lifetime is the access token's. */
export function setConsentMarker(res: NextResponse, marker: ConsentMarker | null, expiresIn: number, secure: boolean): void {
  if (marker) res.cookies.set(CONSENT_COOKIE, `${marker.terms}|${marker.privacy}`, cookieAttributes(secure, expiresIn));
  else res.cookies.set(CONSENT_COOKIE, "", cookieAttributes(secure, 0));
}

export function setRefreshedFlag(res: NextResponse, secure: boolean): void {
  res.cookies.set(REFRESHED_COOKIE, "1", cookieAttributes(secure, 10));
}

export function clearSession(res: NextResponse, secure: boolean): void {
  for (const name of ALL_COOKIES) {
    res.cookies.set(name, "", cookieAttributes(secure, 0));
  }
}

export function setTwoFactorChallenge(res: NextResponse, challengeToken: string, expiresIn: number, secure: boolean): void {
  res.cookies.set(TWO_FACTOR_COOKIE, challengeToken, cookieAttributes(secure, Math.min(Math.max(expiresIn, 1), 300)));
}

export function clearTwoFactorChallenge(res: NextResponse, secure: boolean): void {
  res.cookies.set(TWO_FACTOR_COOKIE, "", cookieAttributes(secure, 0));
}
