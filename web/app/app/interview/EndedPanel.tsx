import { useEffect, useRef } from "react";
import type { InterviewCopy } from "@/lib/messages/interview";
import { Icon } from "../_components/Icon";
import styles from "./interview.module.css";
import type { State } from "./state";

/** The end without a record: stopped by the person, or failed on the service's side (the credit is returned). Nothing is kept. */
export function EndedPanel({ copy, state, onAgain }: { copy: InterviewCopy; state: State; onAgain: () => void }) {
  // The chat is gone; the heading takes the focus, so the outcome is where a keyboard or screen-reader user is.
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    heading.current?.focus();
  }, []);
  return (
    <section aria-labelledby="ended-heading" className={`card ${styles.ended}`}>
      <h2 id="ended-heading" ref={heading} tabIndex={-1}>{copy.chatTitle}</h2>
      <p role="status" className={styles.endedLine}>
        <Icon name="info" size={20} />
        <span>{state.ended === "failed" ? copy.endedFailed : copy.endedStopped}</span>
      </p>
      <div className="actions">
        <button type="button" onClick={onAgain}>{copy.startAnother}</button>
      </div>
    </section>
  );
}
