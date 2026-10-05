import { NextResponse } from "next/server";

// A dedicated probe endpoint: pointing the platform check at `/` would pass on a broken bundle.
export const dynamic = "force-dynamic";

export function GET() {
  return NextResponse.json({ status: "ok" }, { headers: { "Cache-Control": "no-store" } });
}
