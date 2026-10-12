"use client";

import { useId, useState } from "react";
import type { InterviewLanguage, PlatformDraft } from "@/lib/interview-contract";
import type { InterviewCopy } from "@/lib/messages/interview";
import { Icon } from "../_components/Icon";
import styles from "./interview.module.css";

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
  const gated = draft.risk === "high" && !acknowledged;
  const describedBy = gated ? gateId : undefined;
  return (
    <article className={styles.draft} aria-labelledby={headId} lang={language}>
      <header className={styles.draftHead}>
        <h4 id={headId}>{draft.name}</h4>
        <span className={draft.risk === "high" ? `${styles.risk} ${styles.riskHigh}` : styles.risk}>
          <Icon name={draft.risk === "high" ? "alert" : "info"} size={16} />
          {copy.risk[draft.risk]}
        </span>
      </header>
      <p role="note" lang={language} className={styles.draftWarning}>
        {draft.warning}
      </p>
      {draft.risk === "high" ? (
        <div className={styles.gate}>
          <label className="check">
            <input type="checkbox" checked={acknowledged} onChange={(event) => setAcknowledged(event.target.checked)} />
            {copy.draftAcknowledge}
          </label>
          {gated ? (
            <p className="hint" id={gateId}>
              {copy.draftGate}
            </p>
          ) : null}
        </div>
      ) : null}
      {draft.sections.map((section) => {
        const label = section.label ? `${draft.name}, ${section.label}` : draft.name;
        return (
          <div key={section.key} className={styles.section}>
            <div className={styles.sectionHead}>
              {section.label ? <h5>{section.label}</h5> : null}
              <CopyButton copy={copy} label={label} text={section.text} disabled={gated} describedBy={describedBy} />
            </div>
            <p className={styles.text}>{section.text}</p>
          </div>
        );
      })}
      <div className={styles.draftActions}>
        <CopyButton copy={copy} caption={copy.copyAll} text={draft.copyText} disabled={gated} describedBy={describedBy} />
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
      <p className={styles.draftLink}>
        <a href={draft.url} target="_blank" rel="noopener noreferrer">
          {copy.draftOpen(draft.name)} <span className="hint-inline">{copy.opensNewTab}</span>
        </a>
      </p>
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
}: {
  copy: InterviewCopy;
  text: string;
  label?: string;
  caption?: string;
  disabled?: boolean;
  describedBy?: string;
}) {
  const [state, setState] = useState<"idle" | "done" | "failed">("idle");
  async function copyText() {
    try {
      await navigator.clipboard.writeText(text);
      setState("done");
    } catch {
      setState("failed");
    }
  }
  return (
    <div className={styles.copy}>
      <span role="status" className="hint-inline">
        {state === "done" ? copy.copied : state === "failed" ? copy.copyFailed : ""}
      </span>
      <button
        type="button"
        className="secondary small"
        aria-label={label ? `${caption}: ${label}` : undefined}
        aria-describedby={describedBy}
        disabled={disabled}
        onClick={() => void copyText()}
      >
        <Icon name={state === "done" ? "check" : "copy"} size={16} />
        {caption}
      </button>
    </div>
  );
}
