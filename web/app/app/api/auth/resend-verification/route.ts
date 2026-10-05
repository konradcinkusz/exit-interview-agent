import { NextResponse } from "next/server";
import { resendVerification } from "@/lib/identity";
import { authConfig, identityConfigured } from "@/lib/runtime-config";

export const dynamic = "force-dynamic";

const NO_STORE = { "Cache-Control": "no-store" };

/** Always the same answer for a well-formed request, whether or not the address needs verification (no oracle). */
export async function POST(request: Request) {
  const cfg = authConfig();
  if (!identityConfigured(cfg)) return NextResponse.json({ error: "identity_not_configured" }, { status: 503, headers: NO_STORE });

  const body = (await request.json().catch(() => null)) as { email?: unknown } | null;
  const email = typeof body?.email === "string" ? body.email.trim() : "";
  if (!email || email.length > 256) return NextResponse.json({ error: "invalid_request" }, { status: 400, headers: NO_STORE });

  const outcome = await resendVerification(cfg, email);
  if (outcome.ok) return NextResponse.json({ sent: true }, { headers: NO_STORE });
  const status = outcome.error === "rate_limited" ? 429 : outcome.error === "invalid" ? 400 : 502;
  return NextResponse.json({ error: outcome.error }, { status, headers: NO_STORE });
}
