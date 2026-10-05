"use client";

import Link from "next/link";
import { useEffect, useRef } from "react";
import { m } from "@/lib/messages";

/**
 * The error boundary. It prints neither the error message nor its digest: a message can carry data a user typed, and the
 * page must not become a way to read server internals. The cause stays in the server log (without user data).
 */
export default function ErrorPage({ reset }: { error: Error & { digest?: string }; reset: () => void }) {
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => heading.current?.focus(), []);
  return (
    <>
      <h1 ref={heading} tabIndex={-1}>{m.errors.errorTitle}</h1>
      <p role="alert" data-testid="error-page">{m.errors.error}</p>
      <button type="button" onClick={reset}>{m.errors.retry}</button>
      <Link className="button secondary" href="/">{m.errors.home}</Link>
    </>
  );
}
