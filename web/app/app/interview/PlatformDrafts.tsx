"use client";

import { useEffect, useId, useState } from "react";
import type { InterviewLanguage, PlatformDraft } from "@/lib/interview-contract";
import type { InterviewCopy } from "@/lib/messages/interview";
import { Icon } from "../_components/Icon";
import styles from "./interview.module.css";

/** How long "Copied" stays next to a copy button. */
const COPIED_MS = 4000;

interface Props {
  copy: InterviewCopy;
  language: InterviewLanguage;
  drafts: PlatformDraft[];
}

/**
 * One draft per platform (ADR-0083): the platform's name and its risk in words, its warning as a note, each section with its own copy
 * button, "Copy all", the fields the person fills in on the site, and a link to the site. A high-risk draft's copy buttons stay disabled
 * until the warning has been acknowledged on that card. The acknowledgement is component state only: it is not stored and not sent.
 */
export function PlatformDrafts({ copy, language, drafts }: Props) {
  if (drafts.length === 0) return null;
  return (
    <section aria-labelledby="drafts-heading" className={styles.drafts}>
      <h3 id="drafts-heading">{copy.draftsTitle}</h3>
      <p className="hint">{copy.draftsIntro}</p>
      <div className={styles.draftList}>
        {drafts.map((draft) => (
          <DraftCard key={draft.platform} copy={copy} language={language} draft={draft} />
        ))}
      </div>
    </section>
  );
}

function DraftCard({ copy, language, draft }: { copy: InterviewCopy; language: InterviewLanguage; draft: PlatformDraft }) {
  const headId = useId();
  const gateId = useId();
  const [acknowledged, setAcknowledged] = useState(false);
  const high = draft.risk === "high";
  const gated = high && !acknowledged;
  const describedBy = gated ? gateId : undefined;
  // Head (the platform and its risk in words), the warning with its acknowledgement as one callout, the texts, and a footer with
  // "Copy all", the site and what is filled in there. The texts look locked while the gate is closed, but stay fully readable.
  return (
    <article className={high ? `${styles.draft} ${styles.draftHigh}` : styles.draft} aria-labelledby={headId} lang={language}>
      <header className={styles.draftHead}>
        <h4 id={headId}>{draft.name}</h4>
        <span className={high ? `${styles.risk} ${styles.riskHigh}` : styles.risk}>
          <Icon name={high ? "alert" : "info"} size={16} />
          {copy.risk[draft.risk]}
        </span>
      </header>
      <div className={styles.callout}>
        <p role="note" lang={language} className={styles.draftWarning}>
          {draft.warning}
        </p>
        {high ? (
          <div className={styles.gate}>
            <label className={`check ${styles.gateCheck}`}>
              <input type="checkbox" checked={acknowledged} onChange={(event) => setAcknowledged(event.target.checked)} />
              {copy.draftAcknowledge}
            </label>
            {gated ? (
              <p className={styles.gateHint} id={gateId}>
                <Icon name="lock" size={14} />
                {copy.draftGate}
              </p>
            ) : null}
          </div>
        ) : null}
      </div>
      <div className={gated ? `${styles.sections} ${styles.sectionsLocked}` : styles.sections}>
        {draft.sections.map((section) => {
          const label = section.label ? `${draft.name}, ${section.label}` : draft.name;
          return (
            <div key={section.key} className={styles.section}>
              <div className={styles.sectionHead}>
                {section.label ? <h5>{section.label}</h5> : null}
                <CopyButton copy={copy} label={label} text={section.text} disabled={gated} describedBy={describedBy} quiet />
              </div>
              <p className={styles.text}>{section.text}</p>
            </div>
          );
        })}
      </div>
      <footer className={styles.draftFoot}>
        <div className={styles.draftActions}>
          <CopyButton copy={copy} caption={copy.copyAll} text={draft.copyText} disabled={gated} describedBy={describedBy} />
          <a className={styles.draftLink} href={draft.url} target="_blank" rel="noopener noreferrer">
            {copy.draftOpen(draft.name)} <span className="hint-inline">{copy.opensNewTab}</span>
            <Icon name="external" size={16} />
          </a>
        </div>
        {draft.fieldsOnSite.length > 0 ? (
          <div className={styles.fields}>
            <p>{copy.draftFieldsOnSite}</p>
            <ul>
              {draft.fieldsOnSite.map((field) => (
                <li key={field}>{field}</li>
              ))}
            </ul>
          </div>
        ) : null}
      </footer>
    </article>
  );
}

/**
 * Copies one text. The clipboard is the browser's; when it is not available the copy says so, instead of failing quietly. `label` names
 * the text for a screen reader ("Copy: GoWork.pl, Plusy"); without it the visible caption is the name.
 */
export function CopyButton({
  copy,
  text,
  label,
  caption = copy.copy,
  disabled = false,
  describedBy,
  quiet = false,
}: {
  copy: InterviewCopy;
  text: string;
  label?: string;
  caption?: string;
  disabled?: boolean;
  describedBy?: string;
  /** A per-field copy: a lighter button, so a card with many fields does not read as a row of forms. */
  quiet?: boolean;
}) {
  const [state, setState] = useState<"idle" | "done" | "failed">("idle");
  // "Copied" is a moment, not a state: it is announced once (role=status) and goes after a few seconds, so an older copy elsewhere on
  // the page does not keep saying "Copied". A failure stays, because it tells the person what to do instead.
  useEffect(() => {
    if (state !== "done") return;
    const timer = setTimeout(() => setState("idle"), COPIED_MS);
    return () => clearTimeout(timer);
  }, [state]);
  async function copyText() {
    try {
      await navigator.clipboard.writeText(text);
      setState("done");
    } catch {
      setState("failed");
    }
  }
  return (
    <div className={state === "failed" ? `${styles.copy} ${styles.copyFailed}` : styles.copy}>
      <span role="status" className={state === "done" ? `${styles.copyStatus} ${styles.copyDone}` : styles.copyStatus}>
        {state === "done" ? (
          <>
            <Icon name="check" size={14} />
            {copy.copied}
          </>
        ) : state === "failed" ? (
          copy.copyFailed
        ) : (
          ""
        )}
      </span>
      <button
        type="button"
        className={quiet ? `secondary small ${styles.copyQuiet}` : "secondary small"}
        aria-label={label ? `${caption}: ${label}` : undefined}
        aria-describedby={describedBy}
        disabled={disabled}
        data-copied={state === "done" ? "" : undefined}
        onClick={() => void copyText()}
      >
        <Icon name={state === "done" ? "check" : "copy"} size={16} />
        {caption}
      </button>
    </div>
  );
}
