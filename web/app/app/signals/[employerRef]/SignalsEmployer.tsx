"use client";

import { useState } from "react";
import { m } from "@/lib/messages";
import { readEmployer } from "@/lib/signals";
import { employerPath, isEmployerRef } from "@/lib/signals-ref";
import { Failure } from "../Failure";
import { useSignals } from "../useSignals";
import { EmployerView, NothingToShow } from "../views";

const S = m.signals;

/** The reference is checked against the API's pattern first: a string that is not a reference never reaches the network, and looks like every other "nothing to show". */
export function SignalsEmployer({ employerRef }: { employerRef: string }) {
  const [attempt, setAttempt] = useState(0);
  const valid = isEmployerRef(employerRef);
  const resource = useSignals(valid ? employerPath(employerRef) : null, readEmployer, attempt);
  return (
    <>
      <h1>{S.employerTitle}</h1>
      <div aria-live="polite">
        {!valid && <NothingToShow />}
        {valid && resource.kind === "loading" && <p role="status" data-testid="signals-loading">{m.site.loading}</p>}
        {valid && resource.kind === "failure" && (
          <Failure failure={resource.failure} signInRedirect={`/signals/${encodeURIComponent(employerRef)}`} onRetry={() => setAttempt((a) => a + 1)} />
        )}
        {valid && resource.kind === "ok" && <EmployerView employer={resource.data} />}
      </div>
    </>
  );
}
