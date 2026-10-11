"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

/** Ends the session at the BFF (which revokes the refresh tokens), then shows the start page with the header signed out. */
export function SignOutButton({ label, className = "linklike", testId = "header-logout" }: { label: string; className?: string; testId?: string }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  async function signOut() {
    setBusy(true);
    await fetch("/api/auth/session", { method: "DELETE" }).catch(() => null);
    // Push to the start page and re-render the server components, so the header is rendered signed out. Nothing on the start
    // page holds state, so the refresh cannot interrupt anything (sign-in, by contrast, uses a full page load).
    router.push("/");
    router.refresh();
  }
  return (
    <button type="button" className={className} onClick={signOut} disabled={busy} data-testid={testId}>
      {label}
    </button>
  );
}
