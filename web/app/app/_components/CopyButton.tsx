"use client";

import { useState } from "react";

/**
 * Copies a value to the clipboard on click. The value is passed in and never stored here; the clipboard is the browser's,
 * so it is the one place the user has to clear themselves, and the copy says so. Failure (no permission, insecure
 * context) is reported, not hidden.
 */
export function CopyButton(props: { value: string; label: string; done: string; failed: string; testId?: string }) {
  const [state, setState] = useState<"idle" | "done" | "failed">("idle");
  async function copy() {
    try {
      await navigator.clipboard.writeText(props.value);
      setState("done");
    } catch {
      setState("failed");
    }
  }
  return (
    <>
      <button type="button" className="secondary" onClick={copy} data-testid={props.testId}>{props.label}</button>
      <span role="status" className="muted" data-testid={props.testId ? `${props.testId}-status` : undefined}>
        {state === "done" ? props.done : state === "failed" ? props.failed : ""}
      </span>
    </>
  );
}
