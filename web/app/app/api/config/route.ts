import { NextResponse } from "next/server";
import { authConfig, identityConfigured } from "@/lib/runtime-config";

// Dynamic: read the environment per request, never statically optimised (FRONTEND-BFF §2).
export const dynamic = "force-dynamic";

export function GET() {
  // Client-safe values only. Backend addresses never reach the browser: it learns one base path.
  return NextResponse.json(
    { apiBase: "/api/proxy", identity: { enabled: identityConfigured(authConfig()) } },
    { headers: { "Cache-Control": "public, max-age=10, stale-while-revalidate=30" } },
  );
}
