"use client";

import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import { MAX_REPLY_CHARS } from "@/lib/interview-contract";
import type { InterviewCopy } from "@/lib/messages/interview";
import type { InterviewFailure } from "@/lib/interview-api";
import { ConfirmDialog } from "../_components/ConfirmDialog";
import { BrandMark, Icon } from "../_components/Icon";
import styles from "./interview.module.css";
import { Notice } from "./Notice";
import { canFinish, canQuickReply, canRate, type State } from "./state";
import { useCountdown } from "./useCountdown";

interface Props {
  copy: InterviewCopy;
  state: State;
  /** Resolves true when the reply was accepted; false keeps the typed text for another try. */
  onSend: (text: string) => Promise<boolean>;
  /** Asks the interviewer to finish now (the record is kept). */
  onFinish: () => Promise<boolean>;
  onDelete: () => Promise<void>;
  notice: InterviewFailure | null;
}

/** Failures after which the typed answer is still here and sending it again is the way on. */
const RETRYABLE = new Set(["provider_unavailable", "unavailable", "generic", "rate_limited"]);

/**
 * The chat: what kind of conversation this is (a phase line, with no count of topics), the transcript in a live log, on the consent turn
 * the quick answers, one answer box with Send as the only
 * primary action (Enter sends, Shift+Enter is a new line), and two ways out that do different things: "Finish interview" keeps the
 * record, "Stop and delete" wipes everything after a confirmation dialog. The transcript lives in memory only.
 */
export function ChatPanel({ copy, state, onSend, onFinish, onDelete, notice }: Props) {
  const [text, setText] = useState("");
  const [confirming, setConfirming] = useState(false);
  const answer = useRef<HTMLTextAreaElement>(null);
  const log = useRef<HTMLDivElement>(null);

  const composer = useRef<HTMLFormElement>(null);
  const first = useRef(true);

  // The newest message is always in view: the log scrolls itself to the bottom when a line or the typing indicator arrives, and
  // the page brings the composer to its own place, so the sticky composer never sits on top of the newest message. Not on the
  // first render: on a phone that would scroll the title bar and the opening question out of sight before anyone has read them.
  useEffect(() => {
    const el = log.current;
    if (el) el.scrollTop = el.scrollHeight;
    if (first.current) {
      first.current = false;
      return;
    }
    composer.current?.scrollIntoView({ block: "nearest" });
  }, [state.lines.length, state.pending]);
  const waitLeft = useCountdown(notice?.kind === "rate_limited" ? notice.retryAfter : undefined, notice);

  const trimmed = text.trim();
  const tooLong = text.length > MAX_REPLY_CHARS;
  const waiting = waitLeft > 0;
  const canSend = trimmed.length > 0 && !tooLong && !state.pending && !waiting;
  const finishable = canFinish(state) && !waiting;
  const quickReplies = canQuickReply(state);
  const rating = canRate(state);

  async function submit() {
    if (!canSend) return;
    const value = trimmed;
    setText("");
    const accepted = await onSend(value);
    if (!accepted) setText(value);
    answer.current?.focus();
  }

  // A quick answer sends its text as it is and leaves what is typed in the box alone. The group goes once the interviewer has
  // moved on, so focus moves to the answer box first: a keyboard user is not left on a button that has disappeared.
  function quickReply(value: string) {
    void onSend(value);
    answer.current?.focus();
  }

  function onKeyDown(e: KeyboardEvent<HTMLTextAreaElement>) {
    // Enter sends; Shift+Enter is a new line; an Enter that confirms an input-method composition is not a send.
    if (e.key === "Enter" && !e.shiftKey && !e.nativeEvent.isComposing) {
      e.preventDefault();
      void submit();
    }
  }

  return (
    <section aria-labelledby="chat-heading">
      <h2 id="chat-heading" className={styles.visuallyHidden}>{copy.chatTitle}</h2>
      <div className={styles.chat}>
        <div className={styles.chatHead}>
          <span className={styles.chatWho}>
            <BrandMark size={24} />
            {copy.interviewer}
            <span className={styles.aiTag}>AI</span>
          </span>
          <p className={styles.progress} data-testid="chat-progress">{copy.phase}</p>
        </div>
        {/* A div, not a list: role="log" replaces the list semantics of an ol, and the lines are not list items for assistive tech.
            Focusable, because it scrolls: a keyboard user must be able to reach and scroll an earlier question (WCAG 2.1.1). */}
        <div ref={log} className={styles.log} role="log" aria-live="polite" aria-label={copy.conversation} lang={state.language} tabIndex={0}>
          {state.lines.map((line) => (
            <div key={line.id} className={line.from === "you" ? `${styles.row} ${styles.rowYou}` : styles.row}>
              <span className={styles.avatar} aria-hidden="true">
                {line.from === "you" ? <Icon name="user" size={18} /> : <BrandMark size={32} />}
              </span>
              <p className={line.from === "you" ? `${styles.line} ${styles.you}` : styles.line}>
                <span className={styles.who}>
                  {line.from === "you" ? copy.you : copy.interviewer}
                  <span className={styles.visuallyHidden}>: </span>
                </span>
                {line.text}
              </p>
            </div>
          ))}
          {state.pending ? (
            <div className={styles.row} aria-hidden="true">
              <span className={styles.avatar}><BrandMark size={32} /></span>
              <p className={`${styles.line} ${styles.dots}`}><span /><span /><span /></p>
            </div>
          ) : null}
        </div>
        <p role="status" className={styles.typing}>{state.pending ? copy.interviewerTyping : ""}</p>

      {quickReplies ? (
        <div role="group" aria-label={copy.quickRepliesLabel} className={styles.quickReplies} data-testid="quick-replies">
          {copy.quickReplies.map((reply) => (
            <button key={reply} type="button" className={`secondary ${styles.chip}`} disabled={waiting} onClick={() => quickReply(reply)}>
              {reply}
            </button>
          ))}
        </div>
      ) : null}
      {rating ? (
        <div role="group" aria-label={copy.ratingRepliesLabel} className={styles.quickReplies} data-testid="rating-replies">
          <span className={styles.chipsLabel} aria-hidden="true">{copy.ratingRepliesLabel}</span>
          {["1", "2", "3", "4", "5"].map((n) => (
            <button key={n} type="button" className={`secondary ${styles.chip} ${styles.chipNumber}`} disabled={waiting} onClick={() => quickReply(n)}>
              {n}
            </button>
          ))}
        </div>
      ) : null}

      <form
        ref={composer}
        className={styles.composer}
        onSubmit={(e) => {
          e.preventDefault();
          void submit();
        }}
      >
        <Notice failure={notice} copy={copy} waitLeft={waitLeft} />
        <label htmlFor="reply" className={styles.replyLabel}>{copy.replyLabel}</label>
        <textarea
          id="reply"
          ref={answer}
          rows={3}
          value={text}
          lang={state.language}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={onKeyDown}
          aria-describedby="reply-hint reply-count"
          aria-invalid={tooLong}
          placeholder={copy.replyPlaceholder}
        />
        <div className={styles.replyMeta}>
          <div className={styles.replyNotes}>
            <p id="reply-hint" className={`hint ${styles.keysHint}`}>{copy.replyHint}</p>
            <p id="reply-count" className={tooLong ? styles.countOver : "hint"}>{copy.charCount(text.length, MAX_REPLY_CHARS)}</p>
          </div>
          <button type="submit" className="primary" disabled={!canSend}>
            {notice && RETRYABLE.has(notice.kind) && trimmed.length > 0 ? copy.sendAgain : copy.send}
            <Icon name="arrow" size={18} />
          </button>
        </div>
      </form>
      </div>

      <div className={styles.endActions}>
        <div>
          <button type="button" className="secondary" disabled={!finishable} aria-describedby="finish-hint" onClick={() => void onFinish()}>
            <Icon name="flag" size={18} />
            {copy.finish}
          </button>
          <p id="finish-hint" className="hint">{copy.finishHint}</p>
        </div>
        <div>
          <button type="button" className="danger-outline" aria-describedby="stop-hint" onClick={() => setConfirming(true)}>
            <Icon name="trash" size={18} />
            {copy.stopAndDelete}
          </button>
          <p id="stop-hint" className="hint">{copy.stopHint}</p>
        </div>
      </div>

      <ConfirmDialog
        open={confirming}
        title={copy.deleteTitle}
        body={copy.deleteConfirm}
        confirmLabel={copy.deleteYes}
        cancelLabel={copy.cancel}
        onConfirm={() => {
          setConfirming(false);
          void onDelete();
        }}
        onCancel={() => setConfirming(false)}
        testId="delete-dialog"
      />
    </section>
  );
}
