import type { InterviewCopy } from "@/lib/messages/interview";
import type { State } from "./state";

/** The end without a record: stopped by the person, or failed on the service's side (the credit is returned). Nothing is kept. */
export function EndedPanel({ copy, state, onAgain }: { copy: InterviewCopy; state: State; onAgain: () => void }) {
  return (
    <section aria-labelledby="ended-heading">
      <h2 id="ended-heading" tabIndex={-1}>{copy.chatTitle}</h2>
      <p role="status">{state.ended === "failed" ? copy.endedFailed : copy.endedStopped}</p>
      <div className="actions">
        <button type="button" onClick={onAgain}>{copy.startAnother}</button>
      </div>
    </section>
  );
}
