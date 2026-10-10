"use client";

import { useEffect, useRef, useState } from "react";
import type { InterviewFailure } from "@/lib/interview-api";
import { TOPIC_KEYS, type InterviewRecord, type InterviewResult, type RecordTopic, type Tile, type TopicKey } from "@/lib/interview-contract";
import type { InterviewCopy } from "@/lib/messages/interview";
import { tileCopyText, tileLabel, tilesToHtml, tilesToJson } from "@/lib/interview-render";
import { downloadText } from "./download";
import styles from "./interview.module.css";
import { Notice } from "./Notice";
import type { State } from "./state";

interface Props {
  copy: InterviewCopy;
  state: State;
  onDelete: () => Promise<void>;
  notice: InterviewFailure | null;
  onRetry: () => void;
}

/**
 * The result: the legal notice first, the record as text (never as raw JSON), one card per draft text with a copy button, the two
 * downloads, and the delete. The result is held in this component's memory and nowhere else; a reload empties it.
 */
export function ResultPanel({ copy, state, onDelete, notice, onRetry }: Props) {
  const result = state.result;
  const [confirming, setConfirming] = useState(false);
  const cancel = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    if (confirming) cancel.current?.focus();
  }, [confirming]);

  if (!result) {
    return (
      <section aria-labelledby="result-heading">
        <h2 id="result-heading" tabIndex={-1}>{copy.resultTitle}</h2>
        {notice ? (
          <>
            <Notice failure={notice} copy={copy} />
            <button type="button" className="secondary" onClick={onRetry}>{copy.retry}</button>
          </>
        ) : (
          <p role="status">{copy.loadingResult}</p>
        )}
      </section>
    );
  }

  const language = state.language;
  return (
    <section aria-labelledby="result-heading">
      <h2 id="result-heading" tabIndex={-1}>{copy.resultTitle}</h2>
      <p role="status">{copy.endedCompleted}</p>
      <p className="notice legal" role="note" data-testid="tiles-notice">{result.tiles.notice}</p>
      <p className="notice warn">{copy.draftWarning}</p>

      <RecordView copy={copy} result={result} />

      <h3>{copy.tilesTitle}</h3>
      <TilesView copy={copy} language={language} result={result} />

      <div className={styles.actions}>
        <button
          type="button"
          className="secondary"
          onClick={() => downloadText("exit-interview-record.json", JSON.stringify(result.record, null, 2) + "\n", "application/json")}
        >
          {copy.downloadRecord}
        </button>
        <button
          type="button"
          className="secondary"
          onClick={() => downloadText("tiles.html", tilesToHtml(result.tiles, language), "text/html")}
        >
          {copy.downloadTiles}
        </button>
        <button
          type="button"
          className="secondary"
          onClick={() => downloadText("tiles.json", tilesToJson(result.tiles), "application/json")}
        >
          {copy.downloadJson}
        </button>
      </div>

      <Notice failure={notice} copy={copy} />

      {confirming ? (
        <div role="group" aria-labelledby="delete-all-question" className="notice warn">
          <p id="delete-all-question" role="alert">{copy.deleteAllConfirm}</p>
          <div className="actions">
            <button type="button" className="danger" onClick={() => void onDelete()}>{copy.deleteYes}</button>
            <button type="button" className="secondary" ref={cancel} onClick={() => setConfirming(false)}>{copy.cancel}</button>
          </div>
        </div>
      ) : (
        <div className="actions">
          <button type="button" className="danger" onClick={() => setConfirming(true)}>{copy.deleteAll}</button>
        </div>
      )}
    </section>
  );
}

function lookup(map: Record<string, string>, key: string): string {
  return Object.prototype.hasOwnProperty.call(map, key) ? map[key] : key;
}

/** The record in plain sentences: the facts about the interview, then each topic with its rating and its quotes. */
function RecordView({ copy, result }: { copy: InterviewCopy; result: InterviewResult }) {
  const record: InterviewRecord = result.record;
  return (
    <section aria-labelledby="record-heading">
      <h3 id="record-heading">{copy.recordTitle}</h3>
      <p className="muted">{copy.recordIntro}</p>
      <dl className="facts">
        <div>
          <dt>{copy.recordFacts.language}</dt>
          <dd>{lookup(copy.languageNames, record.interview.language)}</dd>
        </div>
        <div>
          <dt>{copy.recordFacts.aiDisclosed}</dt>
          <dd>{record.interview.aiDisclosed ? copy.recordFacts.yes : copy.recordFacts.no}</dd>
        </div>
        <div>
          <dt>{copy.recordFacts.duration}</dt>
          <dd>{lookup(copy.durationBands, record.interview.durationBand)}</dd>
        </div>
        <div>
          <dt>{copy.recordFacts.turns}</dt>
          <dd>{lookup(copy.turnBands, record.interview.turnBand)}</dd>
        </div>
        <div>
          <dt>{copy.recordFacts.tenure}</dt>
          <dd>{lookup(copy.tenure, record.context.tenureBand)}</dd>
        </div>
      </dl>
      {TOPIC_KEYS.map((key: TopicKey) => (
        <TopicView key={key} copy={copy} title={copy.topics[key]} topic={record.topics[key]} />
      ))}
    </section>
  );
}

function TopicView({ copy, title, topic }: { copy: InterviewCopy; title: string; topic: RecordTopic }) {
  const rating = topic.rating === null ? copy.noRating : copy.rating(topic.rating);
  return (
    <section>
      <h4>{title}</h4>
      {topic.status === "no_data" ? (
        <p className="muted">{copy.notDiscussed}</p>
      ) : (
        <>
          <p>
            {rating}
            {topic.confidence ? ` · ${copy.confidence[topic.confidence]}` : ""}
          </p>
          {topic.quotes.length > 0 ? (
            <>
              <p className="muted">{copy.quotes}</p>
              <ul className="plain">
                {topic.quotes.map((quote, i) => (
                  <li key={`${i}-${quote}`}>&ldquo;{quote}&rdquo;</li>
                ))}
              </ul>
            </>
          ) : null}
        </>
      )}
    </section>
  );
}

function TilesView({ copy, language, result }: { copy: InterviewCopy; language: State["language"]; result: InterviewResult }) {
  if (result.tiles.items.length === 0) return <p>{copy.tilesEmpty}</p>;
  return (
    <>
      <ul className={styles.tiles}>
        {result.tiles.items.map((tile: Tile, i: number) => {
          const label = tileLabel(tile.kind, language);
          return (
            <li key={`${i}-${tile.kind}`} className={tile.kind === "reddit" ? `${styles.tile} ${styles.wide}` : styles.tile}>
              <h4>{label}</h4>
              <p className={styles.text}>{tile.text}</p>
              <TileCopy copy={copy} label={label} text={tileCopyText(tile)} />
            </li>
          );
        })}
      </ul>
      {result.tiles.dropped.length > 0 ? <p className="muted">{copy.dropped(result.tiles.dropped.length)}</p> : null}
    </>
  );
}

/** Copies one draft. The clipboard is the browser's; the copy says so when it is not available, instead of failing quietly. */
function TileCopy({ copy, label, text }: { copy: InterviewCopy; label: string; text: string }) {
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
    <div>
      <button type="button" className="secondary" aria-label={`${copy.copy}: ${label}`} onClick={() => void copyText()}>
        {copy.copy}
      </button>{" "}
      <span role="status" className="muted">
        {state === "done" ? copy.copied : state === "failed" ? copy.copyFailed : ""}
      </span>
    </div>
  );
}
