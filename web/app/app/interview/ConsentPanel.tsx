"use client";

import { useEffect, useRef, useState } from "react";
import type { InterviewCopy } from "@/lib/messages/interview";
import type { InterviewFailure } from "@/lib/interview-api";
import { Notice } from "./Notice";
import { useCountdown } from "./useCountdown";
import styles from "./interview.module.css";

interface Props {
  copy: InterviewCopy;
  starting: boolean;
  notice: InterviewFailure | null;
  onStart: () => void;
  onBack: () => void;
}

/**
 * The consent step, before a credit is used: the three things that matter first (an AI; the words go to Anthropic; the result is
 * wiped after 30 minutes), then the full terms, open on request. Starting needs the box ticked; Back uses nothing. After a 429 the
 * start button waits out the seconds the service asked for and then works again; nothing starts by itself.
 */
export function ConsentPanel({ copy, starting, notice, onStart, onBack }: Props) {
  const [agreed, setAgreed] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null);
  const waitLeft = useCountdown(notice?.kind === "rate_limited" ? notice.retryAfter : undefined, notice);
  useEffect(() => {
    heading.current?.focus();
  }, []);

  return (
    <section aria-labelledby="consent-heading">
      <h2 id="consent-heading" tabIndex={-1} ref={heading}>{copy.consentTitle}</h2>
      <p>{copy.consentIntro}</p>
      <ol className={styles.keyPoints} data-testid="consent-key">
        {copy.consentKey.map((point) => (
          <li key={point}>{point}</li>
        ))}
      </ol>
      <details className={styles.terms}>
        <summary>{copy.fullTerms}</summary>
        <ul className="plain" data-testid="consent-terms-full">
          {copy.consentPoints.map((point) => (
            <li key={point}>{point}</li>
          ))}
        </ul>
      </details>
      <label className="check">
        <input type="checkbox" checked={agreed} onChange={(e) => setAgreed(e.target.checked)} />
        <span>{copy.consentCheck}</span>
      </label>
      <Notice failure={notice} copy={copy} waitLeft={waitLeft} />
      <div className="actions">
        <button type="button" className="primary" disabled={!agreed || starting || waitLeft > 0} aria-busy={starting} onClick={onStart}>
          {starting ? copy.loading : copy.startInterview}
        </button>
        <button type="button" className="secondary" onClick={onBack}>{copy.back}</button>
      </div>
    </section>
  );
}
