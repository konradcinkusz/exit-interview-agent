import { headers } from "next/headers";
import { NextResponse } from "next/server";
import { fetchConsentVersions, localeFrom, registerAccount } from "@/lib/identity";
import { authConfig, identityConfigured } from "@/lib/runtime-config";

export const dynamic = "force-dynamic";

const MAX_FIELD = 256;
const NO_STORE = { "Cache-Control": "no-store" };

/**
 * Sign-up through authservice. The versions accepted are the ones authservice has in force at this moment, fetched
 * here, so a page cannot record acceptance of a version it was not shown. No tokens are issued to the browser: the
 * account signs in afterwards, which keeps one path that sets a session. The account has no employer.
 */
export async function POST(request: Request) {
  const cfg = authConfig();
  if (!identityConfigured(cfg)) return NextResponse.json({ error: "identity_not_configured" }, { status: 503, headers: NO_STORE });

  const body = (await request.json().catch(() => null)) as { email?: unknown; password?: unknown; acceptTerms?: unknown; acceptPrivacy?: unknown } | null;
  const email = typeof body?.email === "string" ? body.email.trim() : "";
  const password = typeof body?.password === "string" ? body.password : "";
  if (!email || !password || email.length > MAX_FIELD || password.length > MAX_FIELD) {
    return NextResponse.json({ error: "invalid_request" }, { status: 400, headers: NO_STORE });
  }
  if (body?.acceptTerms !== true || body?.acceptPrivacy !== true) {
    return NextResponse.json({ error: "both_required" }, { status: 400, headers: NO_STORE });
  }

  const versions = await fetchConsentVersions(cfg);
  if (!versions) return NextResponse.json({ error: "identity_unavailable" }, { status: 502, headers: NO_STORE });

  const outcome = await registerAccount(cfg, {
    email,
    password,
    terms: versions.terms,
    privacy: versions.privacy,
    locale: localeFrom((await headers()).get("accept-language")),
  });
  if (outcome.ok) return NextResponse.json({ created: true, verificationRequired: outcome.verificationRequired }, { status: 201, headers: NO_STORE });
  const status = outcome.error === "invalid" ? 400 : outcome.error === "rate_limited" ? 429 : 502;
  return NextResponse.json({ error: outcome.error, messages: outcome.messages }, { status, headers: NO_STORE });
}
