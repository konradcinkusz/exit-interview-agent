"use client";

import { useEffect, useRef, useState } from "react";
import type { InterviewCopy } from "@/lib/messages/interview";
import { Notice } from "./Notice";
import type { InterviewFailure } from "@/lib/interview-api";

interface Props {
  copy: InterviewCopy;
  starting: boolean;
  notice: InterviewFailure | null;
  onStart: () => void;
  onBack: () => void;
}

/**
 * The consent step: the whole disclosure, in the interview's language, before a credit is used. Starting needs the box ticked;
 * declining is the Back button, which uses nothing. The list is the CLI's disclosure, adapted to the hosted service.
 */
export function ConsentPanel({ copy, starting, notice, onStart, onBack }: Props) {
  const [agreed, setAgreed] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    heading.current?.focus();
  }, []);

  return (
    <section aria-labelledby="consent-heading">
      <h2 id="consent-heading" tabIndex={-1} ref={heading}>{copy.consentTitle}</h2>
      <p>{copy.consentIntro}</p>
      <ul className="plain">
        {copy.consentPoints.map((point) => (
          <li key={point}>{point}</li>
        ))}
      </ul>
      <label className="check">
        <input type="checkbox" checked={agreed} onChange={(e) => setAgreed(e.target.checked)} />
        {copy.consentCheck}
      </label>
      <Notice failure={notice} copy={copy} />
      <div className="actions">
        <button type="button" disabled={!agreed || starting} aria-busy={starting} onClick={onStart}>
          {starting ? copy.loading : copy.startInterview}
        </button>
        <button type="button" className="secondary" onClick={onBack}>{copy.back}</button>
      </div>
    </section>
  );
}
