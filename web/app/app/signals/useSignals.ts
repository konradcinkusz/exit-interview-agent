"use client";

import { useEffect, useState } from "react";
import { signalsFailure, type SignalsFailure } from "@/lib/signals";

export type Resource<T> = { kind: "loading" } | { kind: "ok"; data: T } | { kind: "failure"; failure: SignalsFailure };

/**
 * One read of a Signals endpoint through the BFF (`/api/proxy/v1/...`). The browser's own HTTP cache does the caching: the BFF
 * sends `private, max-age` to the next batch and a weak ETag, so within the batch there is no request and afterwards there is a
 * conditional one that costs a 304 (ADR-0068). This hook adds nothing on top: no polling, no timer, no retry loop. A failure
 * stays a failure until the person asks again (`attempt` changes). `null` for `path` means "do not call".
 */
export function useSignals<T>(path: string | null, read: (body: unknown) => T | null, attempt: number): Resource<T> {
  const [state, setState] = useState<{ key: string; resource: Resource<T> } | null>(null);
  const key = `${path}#${attempt}`;

  useEffect(() => {
    if (path === null) return;
    const controller = new AbortController();
    (async () => {
      let response: Response;
      try {
        response = await fetch(`/api/proxy/v1${path}`, { headers: { accept: "application/json" }, signal: controller.signal });
      } catch {
        if (!controller.signal.aborted) setState({ key, resource: { kind: "failure", failure: { kind: "unavailable" } } });
        return;
      }
      const body = (await response.json().catch(() => null)) as unknown;
      if (controller.signal.aborted) return;
      if (response.status === 200) {
        const data = read(body);
        setState({ key, resource: data ? { kind: "ok", data } : { kind: "failure", failure: { kind: "generic" } } });
        return;
      }
      setState({ key, resource: { kind: "failure", failure: signalsFailure(response.status, body, response.headers.get("retry-after")) } });
    })();
    return () => controller.abort();
    // `read` is a module-level function of the caller; the request is identified by the path and the attempt.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [path, attempt]);

  return state?.key === key ? state.resource : { kind: "loading" };
}
