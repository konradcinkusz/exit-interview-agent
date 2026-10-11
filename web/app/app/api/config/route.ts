import { NextResponse } from "next/server";
import { authConfig, devHints, identityConfigured, mcpResourceUrl } from "@/lib/runtime-config";

// Dynamic: read the environment per request, never statically optimised (FRONTEND-BFF §2).
export const dynamic = "force-dynamic";

export function GET() {
  // Client-safe values only. Backend addresses never reach the browser: it learns one base path. `dev.hints` says whether this is
  // the local AppHost stack (DEV_HINTS=1); the test account itself is rendered only by the sign-in page, never by this route.
  return NextResponse.json(
    {
      apiBase: "/api/proxy",
      identity: { enabled: identityConfigured(authConfig()) },
      mcp: { url: mcpResourceUrl() ?? null },
      dev: { hints: devHints() !== null },
    },
    { headers: { "Cache-Control": "public, max-age=10, stale-while-revalidate=30" } },
  );
}
