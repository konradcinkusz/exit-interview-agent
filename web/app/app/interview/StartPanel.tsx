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
  devHints: boolean;
}

/**
 * The first screen: language, time at the company, and one status line that says whether an interview is ready. Continue is the
 * primary action when there is a credit; without one it stays disabled and says why, and buying becomes the next step.
 */
export function StartPanel({ state, dispatch, copy, onBuy, onRetryCredits, devHints }: Props) {
  const [buying, setBuying] = useState(false);
  const balance = state.credits.kind === "ok" ? state.credits.balance : null;
  const canContinue = balance !== null && balance > 0;
  const noCredit = balance === 0;

  return (
    <section aria-labelledby="start-heading">
      <h2 id="start-heading" className={styles.visuallyHidden}>{copy.startSection}</h2>
      <p className="lead">{copy.lead}</p>
      <p className="hint">{copy.notAService}</p>

      <div
        className={`${styles.status} ${canContinue ? styles.statusReady : noCredit ? styles.statusEmpty : ""}`}
        role="status"
        id="credit-status"
        data-testid="credit-status"
      >
        {state.credits.kind === "loading" ? <p>{copy.loadingCredits}</p> : null}
        {state.credits.kind === "failed" ? <p>{failureText({ kind: state.credits.failure }, copy)}</p> : null}
        {canContinue ? (
          <>
            <p className={styles.statusTitle}>{copy.ready(balance)}</p>
            <p>{copy.readyDetail}</p>
          </>
        ) : null}
        {noCredit ? (
          <>
            <p className={styles.statusTitle}>{copy.noCredit}</p>
            <p>{copy.noCreditDetail}</p>
          </>
        ) : null}
      </div>
      {state.credits.kind === "failed" ? (
        <button type="button" className="secondary" onClick={() => void onRetryCredits()}>{copy.retry}</button>
      ) : null}
      {noCredit && devHints ? <p className="dev-hints" data-testid="dev-no-credit">{copy.devNoCredit}</p> : null}

      <fieldset>
        <legend>{copy.language}</legend>
        <div className={styles.choices}>
          {(["pl", "en"] as const).map((language) => (
            <label key={language} className="check" lang={language}>
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
        </div>
      </fieldset>

      <label htmlFor="tenure">{copy.tenureLabel}</label>
      <select id="tenure" value={state.tenure} onChange={(e) => dispatch({ type: "tenure", tenure: e.target.value as Tenure })}>
        {TENURES.map((tenure) => (
          <option key={tenure} value={tenure}>
            {copy.tenure[tenure]}
          </option>
        ))}
      </select>

      <Notice failure={state.notice} copy={copy} />

      <div className="actions">
        <button
          type="button"
          className="primary"
          onClick={() => dispatch({ type: "toConsent" })}
          disabled={!canContinue}
          aria-describedby={!canContinue && state.credits.kind !== "loading" ? "continue-why" : undefined}
        >
          {copy.continue}
        </button>
        <button
          type="button"
          className={noCredit ? "primary" : "secondary"}
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
      {!canContinue && state.credits.kind !== "loading" ? <p id="continue-why" className="hint">{copy.continueNeedsCredit}</p> : null}
      <p id="buy-note" className="hint">{copy.buyNote}</p>
    </section>
  );
}
