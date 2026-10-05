import { NextResponse } from "next/server";
import { verifyEmail } from "@/lib/identity";
import { authConfig, identityConfigured } from "@/lib/runtime-config";

export const dynamic = "force-dynamic";

const MAX_FIELD = 1024; // verification tokens are long
const NO_STORE = { "Cache-Control": "no-store" };

/** Completes an email verification. One answer for "no such account" and "bad token" (authservice's choice, kept). */
export async function POST(request: Request) {
  const cfg = authConfig();
  if (!identityConfigured(cfg)) return NextResponse.json({ error: "identity_not_configured" }, { status: 503, headers: NO_STORE });

  const body = (await request.json().catch(() => null)) as { email?: unknown; token?: unknown } | null;
  const email = typeof body?.email === "string" ? body.email.trim() : "";
  const token = typeof body?.token === "string" ? body.token : "";
  if (!email || !token || email.length > 256 || token.length > MAX_FIELD) {
    return NextResponse.json({ error: "invalid_request" }, { status: 400, headers: NO_STORE });
  }

  const outcome = await verifyEmail(cfg, email, token);
  if (outcome.ok) return NextResponse.json({ verified: true }, { headers: NO_STORE });
  const status = outcome.error === "invalid" ? 400 : outcome.error === "rate_limited" ? 429 : 502;
  return NextResponse.json({ error: outcome.error }, { status, headers: NO_STORE });
}
