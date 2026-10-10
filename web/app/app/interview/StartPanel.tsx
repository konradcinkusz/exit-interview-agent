import { useState } from "react";
import { TENURES, type Tenure } from "@/lib/interview-contract";
import type { InterviewCopy } from "@/lib/messages/interview";
import { Notice, failureText } from "./Notice";
import styles from "./interview.module.css";
import type { Action, State } from "./state";

interface Props {
  state: State;
  dispatch: (action: Action) => void;
  copy: InterviewCopy;
  onBuy: () => Promise<void>;
  onRetryCredits: () => Promise<void>;
}

/** The first screen: language, tenure, the credits the person has, and the way to get one. Continue needs a credit. */
export function StartPanel({ state, dispatch, copy, onBuy, onRetryCredits }: Props) {
  const [buying, setBuying] = useState(false);
  const balance = state.credits.kind === "ok" ? state.credits.balance : null;
  const canContinue = balance !== null && balance > 0;

  let creditLine: string;
  if (state.credits.kind === "loading") creditLine = copy.loadingCredits;
  else if (state.credits.kind === "failed") creditLine = failureText({ kind: state.credits.failure }, copy);
  else creditLine = balance !== null && balance > 0 ? copy.credits(balance) : copy.noCredit;

  return (
    <section aria-labelledby="start-heading">
      <h2 id="start-heading" className={styles.visuallyHidden}>{copy.startSection}</h2>
      <p>{copy.lead}</p>
      <p className="muted">{copy.notAService}</p>

      <fieldset>
        <legend>{copy.language}</legend>
        {(["en", "pl"] as const).map((language) => (
          <label key={language} className="check">
            <input
              type="radio"
              name="language"
              value={language}
              checked={state.language === language}
              onChange={() => dispatch({ type: "language", language })}
            />
            {copy.languageNames[language]}
          </label>
        ))}
      </fieldset>

      <label htmlFor="tenure">{copy.tenureLabel}</label>
      <select id="tenure" value={state.tenure} onChange={(e) => dispatch({ type: "tenure", tenure: e.target.value as Tenure })}>
        {TENURES.map((tenure) => (
          <option key={tenure} value={tenure}>
            {copy.tenure[tenure]}
          </option>
        ))}
      </select>

      <p role="status" className="muted">{creditLine}</p>
      {state.credits.kind === "failed" ? (
        <p>
          <button type="button" className="secondary" onClick={() => void onRetryCredits()}>{copy.retry}</button>
        </p>
      ) : null}

      <Notice failure={state.notice} copy={copy} />

      <div className="actions">
        <button type="button" onClick={() => dispatch({ type: "toConsent" })} disabled={!canContinue}>
          {copy.continue}
        </button>
        <button
          type="button"
          className={canContinue ? "secondary" : undefined}
          disabled={buying}
          aria-describedby="buy-note"
          onClick={async () => {
            setBuying(true);
            await onBuy();
            setBuying(false);
          }}
        >
          {buying ? copy.buying : copy.buy}
        </button>
      </div>
      <p id="buy-note" className="muted">{copy.buyNote}</p>
    </section>
  );
}
