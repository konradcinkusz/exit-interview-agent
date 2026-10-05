import { NextResponse } from "next/server";
import { NO_STORE, callBackend } from "@/lib/upstream";

export const dynamic = "force-dynamic";

// The receipt-code header and the name it travels under are the T5 contract (ADR-0029): the code goes in a header, never a URL.
const RECEIPT_HEADER = "X-Receipt-Code";
// A well-formed code is 46 characters; anything past this is refused without asking the backend.
const MAX_CODE_LENGTH = 128;

const problem = (status: number, code: string, title: string) =>
  NextResponse.json({ type: "about:blank", title, status, code }, { status, headers: { ...NO_STORE, "content-type": "application/problem+json" } });

/**
 * Deletion by receipt code. ANONYMOUS on purpose (ADR-0049): the code is the only credential, it must work after the
 * account is gone, and no session cookie is read, so the request can never be tied to an account here.
 *
 * It forwards exactly one header, `X-Receipt-Code`, to exactly one backend route, `DELETE /api/v1/receipts`, with no
 * bearer, no cookie and no query string. The answer (204 for every well-formed code, 400 INVALID_RECEIPT_CODE, 429) is
 * passed through unchanged. The code is never logged, stored or echoed.
 */
export async function DELETE(request: Request) {
  const code = request.headers.get(RECEIPT_HEADER)?.trim() ?? "";
  if (!code || code.length > MAX_CODE_LENGTH) return problem(400, "INVALID_RECEIPT_CODE", "The receipt code is not valid.");

  const result = await callBackend({
    backend: "interview-service",
    upstreamPath: "/api/v1/receipts",
    method: "DELETE",
    headers: new Headers({ [RECEIPT_HEADER]: code, accept: "application/json" }),
  });
  if (result === "backend_unavailable") return problem(503, "BACKEND_UNAVAILABLE", "The service could not be reached.");
  return result;
}
