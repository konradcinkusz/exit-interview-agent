"use client";

import { useState } from "react";
import type { InterviewFailure } from "@/lib/interview-api";
import { TOPIC_KEYS, type InterviewRecord, type InterviewResult, type RecordTopic, type Tile, type TopicKey } from "@/lib/interview-contract";
import { interviewCopy, type InterviewCopy } from "@/lib/messages/interview";
import { ConfirmDialog } from "../_components/ConfirmDialog";
import { Icon } from "../_components/Icon";
import { tileCopyText, tileLabel, tilesToHtml, tilesToJson } from "@/lib/interview-render";
import { downloadText } from "./download";
import styles from "./interview.module.css";
import { Notice } from "./Notice";
import { CopyButton, PlatformDrafts } from "./PlatformDrafts";
import type { State } from "./state";

interface Props {
  copy: InterviewCopy;
  state: State;
  onDelete: () => Promise<void>;
  notice: InterviewFailure | null;
  onRetry: () => void;
}

const RESULT_LIFETIME_MS = 30 * 60_000;

/**
 * The result: when it is wiped, one warning (the service's legal notice, under "drafts, not facts"), the record as text (never as raw JSON), one card per draft text with a copy button, the two
 * downloads, and the delete. The result is held in this component's memory and nowhere else; a reload empties it.
 */
export function ResultPanel({ copy, state, onDelete, notice, onRetry }: Props) {
  const result = state.result;
  const [confirming, setConfirming] = useState(false);

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
          <div className="card">
            <p role="status">{copy.loadingResult}</p>
            <span className="skeleton" aria-hidden="true" />
            <span className="skeleton mid" aria-hidden="true" />
            <span className="skeleton short" aria-hidden="true" />
          </div>
        )}
      </section>
    );
  }

  const language = state.language;
  // The service wipes the result 30 minutes after the interview ended. The page knows when it saw the end, so it can name a time.
  const until = state.endedAt === null ? null : new Date(state.endedAt + RESULT_LIFETIME_MS);
  const untilText = until ? until.toLocaleTimeString(copy === interviewCopy.pl ? "pl-PL" : "en-GB", { hour: "2-digit", minute: "2-digit" }) : null;
  return (
    <section aria-labelledby="result-heading">
      <h2 id="result-heading" tabIndex={-1}>{copy.resultTitle}</h2>
      <div className={`card ${styles.summary}`} data-testid="result-summary">
        <span className={styles.summaryIcon}><Icon name="check" size={22} /></span>
        <div>
          <p role="status" className={styles.summaryTitle}>{copy.endedCompleted}</p>
          <p className={styles.summaryLine}>
            <Icon name="clock" size={18} />
            <span>{untilText ? copy.keptUntil(untilText) : copy.keptFor}</span>
          </p>
          <p className="hint">{copy.creditUsed}</p>
        </div>
      </div>

      <section className={`notice warn ${styles.warning}`} aria-labelledby="warning-heading" data-testid="result-warning">
        <Icon name="alert" size={22} className={styles.warningIcon} />
        <div>
          <h3 id="warning-heading" className={styles.noticeTitle}>{copy.warningTitle}</h3>
          <p data-testid="tiles-notice" lang={language}>{result.tiles.notice}</p>
        </div>
      </section>

      <RecordView copy={copy} result={result} />

      {result.tiles.items.length > 0 || result.tiles.drafts.length === 0 ? (
        <>
          <h3>{copy.tilesTitle}</h3>
          <TilesView copy={copy} language={language} result={result} />
        </>
      ) : null}
      <PlatformDrafts copy={copy} language={language} drafts={result.tiles.drafts} />
      {result.tiles.dropped.length > 0 ? <p className="hint" data-testid="tiles-dropped">{copy.dropped(result.tiles.dropped.length)}</p> : null}

      <div className={styles.resultActions}>
      <details className={styles.save} data-testid="save">
        <summary className="button primary">
          <Icon name="download" size={18} />
          {copy.save}
        </summary>
        <p className="hint">{copy.saveHint}</p>
        <div className={styles.saveOptions}>
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
      </details>
        <button type="button" className="danger-outline" onClick={() => setConfirming(true)}>
          <Icon name="trash" size={18} />
          {copy.deleteAll}
        </button>
      </div>

      <Notice failure={notice} copy={copy} />
      <ConfirmDialog
        open={confirming}
        title={copy.deleteTitle}
        body={copy.deleteAllConfirm}
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

function lookup(map: Record<string, string>, key: string): string {
  return Object.prototype.hasOwnProperty.call(map, key) ? map[key] : key;
}

/** The record in plain sentences: the facts about the interview, then each topic with its rating and its quotes. */
function RecordView({ copy, result }: { copy: InterviewCopy; result: InterviewResult }) {
  const record: InterviewRecord = result.record;
  const notDiscussed = TOPIC_KEYS.filter((key) => record.topics[key].status === "no_data");
  return (
    <section aria-labelledby="record-heading">
      <h3 id="record-heading">{copy.recordTitle}</h3>
      <p className="hint">{copy.recordIntro}</p>
      <dl className={styles.facts}>
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
      <div className={styles.topics}>
        {TOPIC_KEYS.filter((key) => record.topics[key].status !== "no_data").map((key: TopicKey) => (
          <TopicView key={key} copy={copy} title={copy.topics[key]} topic={record.topics[key]} />
        ))}
      </div>
      {notDiscussed.length > 0 ? (
        <p className="hint" data-testid="not-discussed">{copy.notDiscussedLine(notDiscussed.map((key) => copy.topics[key]).join(", "))}</p>
      ) : null}
    </section>
  );
}

function TopicView({ copy, title, topic }: { copy: InterviewCopy; title: string; topic: RecordTopic }) {
  const rating = topic.rating === null ? copy.noRating : copy.rating(topic.rating);
  return (
    <section className={styles.topic}>
      <h4>{title}</h4>
      {topic.status === "no_data" ? (
        <p className="hint">{copy.notDiscussed}</p>
      ) : (
        <>
          <p className={styles.rating}>
            {topic.rating !== null ? (
              <span className={styles.meter} aria-hidden="true">
                {[1, 2, 3, 4, 5].map((n) => <span key={n} className={n <= (topic.rating ?? 0) ? styles.on : undefined} />)}
              </span>
            ) : null}
            <span>
              {rating}
              {topic.confidence ? ` · ${copy.confidence[topic.confidence]}` : ""}
            </span>
          </p>
          {topic.quotes.length > 0 ? (
            <>
              <p className="hint">{copy.quotes}</p>
              <ul className={styles.quotes}>
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
      <ul className={styles.tiles} lang={language}>
        {result.tiles.items.map((tile: Tile, i: number) => {
          const label = tileLabel(tile.kind, language);
          return (
            <li key={`${i}-${tile.kind}`} className={styles.tile}>
              <div className={styles.tileHead}>
                <h4>{label}</h4>
                <CopyButton copy={copy} label={label} text={tileCopyText(tile)} quiet />
              </div>
              <p className={styles.text}>{tile.text}</p>
            </li>
          );
        })}
      </ul>
    </>
  );
}
