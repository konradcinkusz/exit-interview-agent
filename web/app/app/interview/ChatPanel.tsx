"use client";

import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import { MAX_REPLY_CHARS } from "@/lib/interview-contract";
import type { InterviewCopy } from "@/lib/messages/interview";
import type { InterviewFailure } from "@/lib/interview-api";
import styles from "./interview.module.css";
import { Notice } from "./Notice";
import type { State } from "./state";

interface Props {
  copy: InterviewCopy;
  state: State;
  /** Resolves true when the reply was accepted; false keeps the typed text for another try. */
  onSend: (text: string) => Promise<boolean>;
  onDelete: () => Promise<void>;
  notice: InterviewFailure | null;
}

/**
 * The chat: the transcript in a live log, one answer box, Enter to send (Shift+Enter for a new line), a "typing" line while the
 * interviewer answers, and the one control that ends and wipes everything. The transcript lives in this component's state only.
 */
export function ChatPanel({ copy, state, onSend, onDelete, notice }: Props) {
  const [text, setText] = useState("");
  const [confirming, setConfirming] = useState(false);
  const cancel = useRef<HTMLButtonElement>(null);
  const answer = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    if (confirming) cancel.current?.focus();
  }, [confirming]);

  const trimmed = text.trim();
  const tooLong = text.length > MAX_REPLY_CHARS;
  const canSend = trimmed.length > 0 && !tooLong && !state.pending;

  async function submit() {
    if (!canSend) return;
    const value = trimmed;
    setText("");
    const accepted = await onSend(value);
    if (!accepted) setText(value);
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
      {/* A div, not a list: role="log" replaces the list semantics of an ol, and the lines are not list items for assistive tech. */}
      <div className={styles.log} role="log" aria-live="polite" aria-label={copy.conversation}>
        {state.lines.map((line) => (
          <p key={line.id} className={line.from === "you" ? `${styles.line} ${styles.you}` : styles.line}>
            <span className={styles.visuallyHidden}>{line.from === "you" ? `${copy.you}: ` : `${copy.interviewer}: `}</span>
            {line.text}
          </p>
        ))}
      </div>
      <p role="status" className="muted">{state.pending ? copy.interviewerTyping : ""}</p>

      <form
        onSubmit={(e) => {
          e.preventDefault();
          void submit();
        }}
      >
        <label htmlFor="reply">{copy.replyLabel}</label>
        <textarea
          id="reply"
          ref={answer}
          rows={3}
          value={text}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={onKeyDown}
          aria-describedby="reply-hint reply-count"
          aria-invalid={tooLong}
        />
        <p id="reply-hint" className="muted">{copy.replyHint}</p>
        <p id="reply-count" className={tooLong ? "notice warn" : "muted"}>{copy.charCount(text.length, MAX_REPLY_CHARS)}</p>
        <div className="actions">
          <button type="submit" disabled={!canSend}>{copy.send}</button>
        </div>
      </form>

      <Notice failure={notice} copy={copy} />

      {confirming ? (
        <div role="group" aria-labelledby="delete-question" className="notice warn">
          <p id="delete-question" role="alert">{copy.deleteConfirm}</p>
          <div className="actions">
            <button type="button" className="danger" onClick={() => void onDelete()}>{copy.deleteYes}</button>
            <button type="button" className="secondary" ref={cancel} onClick={() => setConfirming(false)}>{copy.cancel}</button>
          </div>
        </div>
      ) : (
        <div className="actions">
          <button type="button" className="danger" onClick={() => setConfirming(true)}>{copy.stopAndDelete}</button>
        </div>
      )}
    </section>
  );
}
