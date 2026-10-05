import { NextResponse } from "next/server";
import { fetchConsentVersions } from "@/lib/identity";
import { authConfig, identityConfigured } from "@/lib/runtime-config";

export const dynamic = "force-dynamic";

/** The Terms and Privacy versions a registration must accept (authservice publishes them anonymously). Nothing secret. */
export async function GET() {
  const cfg = authConfig();
  if (!identityConfigured(cfg)) return NextResponse.json({ error: "identity_not_configured" }, { status: 503 });
  const versions = await fetchConsentVersions(cfg);
  return versions ? NextResponse.json(versions) : NextResponse.json({ error: "identity_unavailable" }, { status: 502 });
}
