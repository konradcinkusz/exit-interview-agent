import { cookies } from "next/headers";
import { NextResponse } from "next/server";
import { verifyAccessToken } from "@/lib/jwt";
import { authConfig } from "@/lib/runtime-config";
import { ACCESS_COOKIE, clearSession } from "@/lib/session";

export const dynamic = "force-dynamic";

/** Rehydrates client state on page load: client JS cannot read the HttpOnly cookie, by design. */
export async function GET() {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  const payload = await verifyAccessToken(token, authConfig());
  const body = payload ? { authenticated: true, subject: payload.sub } : { authenticated: false };
  return NextResponse.json(body, { headers: { "Cache-Control": "no-store" } });
}

/** Logout: deletes with the same attributes the cookies were set with. */
export function DELETE() {
  const response = NextResponse.json({ authenticated: false });
  clearSession(response, authConfig().secureCookies);
  return response;
}
