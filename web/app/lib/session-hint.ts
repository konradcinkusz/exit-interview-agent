import { cookies } from "next/headers";
import { ACCESS_COOKIE, REFRESH_COOKIE } from "./session";

/**
 * Whether the request carries a session cookie, for choosing what a page OFFERS (sign in, or start the interview). A display
 * hint only, never authorization: a lapsed or forged cookie still meets the edge gate and the APIs.
 */
export async function hasSessionCookie(): Promise<boolean> {
  const jar = await cookies();
  return Boolean(jar.get(ACCESS_COOKIE)?.value || jar.get(REFRESH_COOKIE)?.value);
}
