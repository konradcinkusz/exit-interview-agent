import { cookies } from "next/headers";
import { NextResponse } from "next/server";
import { deleteAccount } from "@/lib/identity";
import { authConfig } from "@/lib/runtime-config";
import { ACCESS_COOKIE, REFRESH_COOKIE, clearSession } from "@/lib/session";
import { resolveAccessToken } from "@/lib/session-flow";

export const dynamic = "force-dynamic";

const STATUS: Record<string, number> = {
  password_required: 400,
  invalid_password: 400,
  confirmation_required: 400,
  unauthenticated: 401,
  identity_unavailable: 502,
};

/**
 * Account deletion through authservice (soft delete, sessions revoked). It deletes the LOGIN, never a submission:
 * records are not linked to accounts by design, so there is nothing here that could find them (ADR-0013).
 * The password and the confirmation text pass straight through and are never stored or logged.
 */
export async function DELETE(request: Request) {
  const jar = await cookies();
  const cfg = authConfig();
  const resolved = await resolveAccessToken({ access: jar.get(ACCESS_COOKIE)?.value, refresh: jar.get(REFRESH_COOKIE)?.value }, cfg);
  if (resolved.kind === "unavailable") return NextResponse.json({ error: "identity_unavailable" }, { status: 502 });
  if (resolved.kind === "none") return NextResponse.json({ error: "unauthenticated" }, { status: 401 });

  const body = (await request.json().catch(() => null)) as { password?: unknown; confirmation?: unknown } | null;
  const confirmation = typeof body?.confirmation === "string" ? body.confirmation : "";
  const password = typeof body?.password === "string" ? body.password : undefined;
  if (confirmation !== "DELETE" || (password !== undefined && password.length > 256)) {
    return NextResponse.json({ error: "confirmation_required" }, { status: 400 });
  }

  const outcome = await deleteAccount(cfg, resolved.accessToken, { password, confirmation });
  if (!outcome.ok) return NextResponse.json({ error: outcome.error }, { status: STATUS[outcome.error] ?? 502 });

  const response = NextResponse.json({ deleted: true });
  clearSession(response, cfg.secureCookies);
  return response;
}
