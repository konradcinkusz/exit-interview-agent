"use client";

import { useState } from "react";
import { m } from "@/lib/messages";
import { PAGE_SIZE, readEmployerList } from "@/lib/signals";
import { Failure } from "./Failure";
import { useSignals } from "./useSignals";
import { EmployerListView } from "./views";

const S = m.signals;

export function SignalsList({ page }: { page: number }) {
  const [attempt, setAttempt] = useState(0);
  const resource = useSignals(`/signals/employers?page=${page}&limit=${PAGE_SIZE}`, readEmployerList, attempt);
  return (
    <>
      <h1>{S.title}</h1>
      <p>{S.lead}</p>

      <section aria-labelledby="explainer-heading" data-testid="signals-explainer">
        <h2 id="explainer-heading">{S.explainerTitle}</h2>
        <ul className="plain">{S.explainer.map((line) => <li key={line}>{line}</li>)}</ul>
      </section>

      <div aria-live="polite">
        {resource.kind === "loading" && <p role="status" data-testid="signals-loading">{m.site.loading}</p>}
        {resource.kind === "failure" && (
          <Failure failure={resource.failure} signInRedirect={page > 1 ? `/signals?page=${page}` : "/signals"} onRetry={() => setAttempt((a) => a + 1)} />
        )}
        {resource.kind === "ok" && <EmployerListView list={resource.data} />}
      </div>
    </>
  );
}
