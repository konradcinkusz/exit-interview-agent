import { cookies } from "next/headers";
import { NextResponse } from "next/server";
import { fetchExport } from "@/lib/identity";
import { authConfig } from "@/lib/runtime-config";
import { ACCESS_COOKIE, REFRESH_COOKIE } from "@/lib/session";
import { resolveAccessToken } from "@/lib/session-flow";

export const dynamic = "force-dynamic";

const NO_STORE = { "Cache-Control": "no-store" };

/**
 * "Download my data": authservice's export (GDPR Art. 15 and 20) of what IT holds about the signed-in account, streamed
 * with its attachment name. It contains no submission, because submissions are not linked to accounts and nothing here
 * could find them; the account page says so next to the button. Gated by the edge gate and by the bearer check here.
 */
export async function GET() {
  const jar = await cookies();
  const cfg = authConfig();
  const resolved = await resolveAccessToken({ access: jar.get(ACCESS_COOKIE)?.value, refresh: jar.get(REFRESH_COOKIE)?.value }, cfg);
  if (resolved.kind === "unavailable") return NextResponse.json({ error: "identity_unavailable" }, { status: 502, headers: NO_STORE });
  if (resolved.kind === "none") return NextResponse.json({ error: "unauthenticated" }, { status: 401, headers: NO_STORE });

  const upstream = await fetchExport(cfg, resolved.accessToken);
  if (!upstream) return NextResponse.json({ error: "identity_unavailable" }, { status: 502, headers: NO_STORE });
  if (!upstream.ok) return NextResponse.json({ error: upstream.status === 401 ? "unauthenticated" : "identity_error" }, { status: upstream.status === 401 ? 401 : 502, headers: NO_STORE });

  const headers = new Headers(NO_STORE);
  headers.set("content-type", upstream.headers.get("content-type") ?? "application/json");
  headers.set("content-disposition", upstream.headers.get("content-disposition") ?? 'attachment; filename="account-export.json"');
  return new NextResponse(upstream.body, { status: 200, headers });
}
