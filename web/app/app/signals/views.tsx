import Link from "next/link";
import { m } from "@/lib/messages";
import {
  fill, formatBatchStart, levelPercent, twoDecimals,
  type Cell, type Cut, type Employer, type EmployerList, type Group, type Snapshot, type Stats, type Topic,
} from "@/lib/signals";
import { employerPagePath } from "@/lib/signals-ref";

// Presentational components of the Signals pages. They take what the API returned (already read by `lib/signals.ts`) and
// render it as it is. Nothing here computes, sorts, ranks, compares or derives: not a total, an average, a percentage, a
// difference, a rounding policy or an order. The one piece of arithmetic is the position of a value on the fixed 1-5 axis
// of the range bar, which is geometry. There is no `dangerouslySetInnerHTML`, no URL built from an API string except through
// `employerPagePath` (validated, then encoded), and every API string is rendered as a React text child (ADR-0067).

const S = m.signals;
const label = (map: Record<string, string>, key: string) => map[key] ?? key;

export function SnapshotNote({ snapshot }: { snapshot: Snapshot }) {
  return (
    <div data-testid="snapshot">
      <p>
        <time dateTime={snapshot.generatedAt}>{fill(S.snapshot, { date: formatBatchStart(snapshot.generatedAt) })}</time>
      </p>
      <p className="muted">{fill(S.snapshotInterval, { hours: snapshot.publicationIntervalHours })}</p>
    </div>
  );
}

export function AggregatedNote() {
  return <p className="notice" data-testid="aggregated-note">{S.aggregatedNote}</p>;
}

export function NothingToShow({ backHref = "/signals" }: { backHref?: string }) {
  return (
    <section data-testid="nothing-to-show">
      <p className="notice" role="status"><strong>{S.nothing}</strong> {S.nothingDetail}</p>
      <p><Link href={backHref}>{S.backToList}</Link></p>
    </section>
  );
}

export function EmployerListView({ list }: { list: EmployerList }) {
  const empty = list.snapshot === null || list.total === 0;
  const lastPage = Math.max(1, Math.ceil(list.total / list.limit));
  const previous = list.page > 1 ? Math.min(list.page - 1, lastPage) : null;
  const hasNext = list.page * list.limit < list.total;
  return (
    <>
      <AggregatedNote />
      {list.snapshot && <SnapshotNote snapshot={list.snapshot} />}
      {empty ? (
        <p className="notice" role="status" data-testid="signals-empty"><strong>{S.empty}</strong> {S.emptyDetail}</p>
      ) : (
        <>
          <h2>{S.listTitle}</h2>
          <p data-testid="list-order">{S.listOrder}</p>
          <ul className="plain employers" aria-label={S.listLabel} data-testid="employer-list">
            {list.employers.map((ref) => {
              const href = employerPagePath(ref);
              return <li key={ref}>{href ? <Link href={href}>{ref}</Link> : ref}</li>;
            })}
          </ul>
          {(previous !== null || hasNext) && (
            <nav aria-label={S.paging} className="paging" data-testid="paging">
              {previous !== null && <Link href={previous === 1 ? "/signals" : `/signals?page=${previous}`} rel="prev">{S.previous}</Link>}
              <span className="muted">{fill(S.pageLabel, { page: list.page })}</span>
              {hasNext && <Link href={`/signals?page=${list.page + 1}`} rel="next">{S.next}</Link>}
            </nav>
          )}
        </>
      )}
    </>
  );
}

/** The interval and the mean on the fixed 1-5 scale. Decorative (`aria-hidden`): the same values are text beside it. It encodes nothing about n. */
export function RangeBar({ stats }: { stats: Stats }) {
  const at = (v: number) => ((Math.min(5, Math.max(1, v)) - 1) / 4) * 100;
  const from = at(stats.lower);
  const to = at(stats.upper);
  return (
    <svg className="range-bar" viewBox="0 0 100 14" preserveAspectRatio="none" aria-hidden="true" focusable="false" data-testid="range-bar">
      <line className="axis" x1="0" y1="7" x2="100" y2="7" />
      <rect className="span" x={from} y="3" width={Math.max(to - from, 0.5)} height="8" />
      <line className="mean" x1={at(stats.mean)} y1="1" x2={at(stats.mean)} y2="13" />
    </svg>
  );
}

/** The one line a figure is always read in: the mean with its interval and n. Never rendered without all of them. */
export function StatLine({ stats }: { stats: Stats }) {
  return (
    <span className="stat-line" data-testid="stat-line">
      {fill(S.statLine, { mean: twoDecimals(stats.mean), level: levelPercent(stats.level), lower: twoDecimals(stats.lower), upper: twoDecimals(stats.upper), n: stats.n })}
    </span>
  );
}

function Reliability({ stats }: { stats: Stats }) {
  return <>{S.reliabilityLabel}: {S.reliability[stats.reliability]}</>;
}
function CoverageText({ stats }: { stats: Stats }) {
  return <>{S.coverageLabel}: {S.coverage[stats.coverage]}</>;
}

function GroupTable({ caption, head, labels, groups, testId }: { caption: string; head: string; labels: Record<string, string>; groups: Group[]; testId: string }) {
  return (
    <table className="signals-table" data-testid={testId}>
      <caption>{caption}</caption>
      <thead><tr><th scope="col">{head}</th><th scope="col" className="num">{S.distributionCount}</th></tr></thead>
      <tbody>
        {groups.map((g) => (
          <tr key={g.key}><th scope="row">{label(labels, g.key)}</th><td className="num">{g.count}</td></tr>
        ))}
      </tbody>
    </table>
  );
}

function CellRow({ cell }: { cell: Cell }) {
  return (
    <tr data-testid="cut-band">
      <th scope="row">{label(S.bands, cell.band)}</th>
      <td>
        {cell.status === "ok" && (
          <>
            <StatLine stats={cell.stats} />
            <span className="line"><Reliability stats={cell.stats} /></span>
            <span className="line"><CoverageText stats={cell.stats} /></span>
          </>
        )}
        {cell.status === "none" && S.cutNone}
        {cell.status === "suppressed" && S.cutBandSuppressed}
      </td>
    </tr>
  );
}

function CutView({ cut }: { cut: Cut }) {
  const name = label(S.dimensions, cut.dimension);
  if (cut.status === "suppressed") {
    return (
      <div data-testid={`cut-${cut.dimension}`} data-state="suppressed">
        <h4>{fill(S.cutCaption, { dimension: name })}</h4>
        <p className="muted" data-testid="cut-suppressed">{S.cutSuppressed}</p>
      </div>
    );
  }
  return (
    <div data-testid={`cut-${cut.dimension}`} data-state="published">
      <table className="signals-table">
        <caption>{fill(S.cutCaption, { dimension: name })}</caption>
        <thead><tr><th scope="col">{S.cutGroup}</th><th scope="col">{S.cutFigures}</th></tr></thead>
        <tbody>{cut.cells.map((cell) => <CellRow key={cell.band} cell={cell} />)}</tbody>
      </table>
    </div>
  );
}

function TopicView({ topic }: { topic: Topic }) {
  const id = `topic-${topic.topic}`;
  const name = label(S.topics, topic.topic);
  if (topic.status === "insufficient_data") {
    return (
      <section aria-labelledby={id} data-testid={id} data-state="insufficient_data">
        <h2 id={id}>{name}</h2>
        <p data-testid="not-enough">{S.notEnough}</p>
      </section>
    );
  }
  const o = topic.overall;
  return (
    <section aria-labelledby={id} data-testid={id} data-state="ok">
      <h2 id={id}>{name}</h2>
      <p className="stat"><StatLine stats={o} /></p>
      <RangeBar stats={o} />
      <p className="muted">{S.wideInterval}</p>
      <dl className="facts">
        <div><dt>{S.reliabilityLabel}</dt><dd data-testid="reliability">{S.reliability[o.reliability]}</dd></div>
        <div><dt>{S.coverageLabel}</dt><dd data-testid="coverage">{S.coverage[o.coverage]}</dd></div>
      </dl>
      {o.distribution && o.distribution.length > 0 && (
        <GroupTable caption={S.distributionCaption} head={S.distributionGroup} labels={S.distribution} groups={o.distribution} testId="distribution" />
      )}
      {o.verification && o.verification.length > 0 && (
        <>
          <p className="muted" data-testid="verification-note">{S.verificationNote}</p>
          <GroupTable caption={S.verificationCaption} head={S.verificationGroup} labels={S.verification} groups={o.verification} testId="verification" />
        </>
      )}
      <h3>{S.cutsTitle}</h3>
      <p className="muted">{S.cutsNote}</p>
      {topic.cuts.map((cut) => <CutView key={cut.dimension} cut={cut} />)}
    </section>
  );
}

export function EmployerView({ employer }: { employer: Employer }) {
  return (
    <>
      <p data-testid="employer-ref"><span className="muted">{S.employerLabel}:</span> <code>{employer.employerRef}</code></p>
      <AggregatedNote />
      <SnapshotNote snapshot={employer.snapshot} />
      <dl className="facts">
        <div><dt>{S.respondents}</dt><dd data-testid="respondents">{employer.respondentsBand} <span className="muted">{S.respondentsNote}</span></dd></div>
      </dl>
      <p className="muted">{fill(S.minimumNote, { k: employer.snapshot.minimumGroupSize })}</p>
      {employer.topics.map((topic) => <TopicView key={topic.topic} topic={topic} />)}
      <p><Link href="/signals">{S.backToList}</Link></p>
    </>
  );
}
