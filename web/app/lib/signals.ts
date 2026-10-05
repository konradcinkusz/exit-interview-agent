// Types, strict readers and failure mapping for the Signals API (src/ExitInterviewAgent.Contracts/SignalsContracts.cs).
//
// What this file does NOT do, on purpose (docs/privacy/AGGREGATION.md section 8, ADR-0067): it computes nothing. No sum, mean,
// rounding to hide an interval, sort, rank, comparison or percentage exists here or in the components. Values are read and
// handed on exactly as the API returned them. The types make the forbidden states unrepresentable: a topic with
// `insufficient_data` has no numbers in it, a `suppressed` cut has no cells, a `none` or `suppressed` band has no stats,
// so a view cannot show what the reader must not see even if a response carried it by mistake.

export interface Snapshot {
  generatedAt: string;
  publicationIntervalHours: number;
  rulesVersion: string;
  minimumGroupSize: number;
  deletionsAppearAtNextPublication: boolean;
}

export interface Group {
  key: string;
  count: number;
}

export type Reliability = "low" | "moderate" | "high";
export type Coverage = "low" | "medium" | "high";

export interface Stats {
  n: number;
  mean: number;
  lower: number;
  upper: number;
  /** The interval's confidence level, e.g. 0.95. */
  level: number;
  reliability: Reliability;
  coverage: Coverage;
  distribution: Group[] | null;
  verification: Group[] | null;
}

export type Cell = { band: string; status: "ok"; stats: Stats } | { band: string; status: "none" } | { band: string; status: "suppressed" };
export type Cut = { dimension: string; status: "published"; cells: Cell[] } | { dimension: string; status: "suppressed" };
export type Topic = { topic: string; status: "ok"; overall: Stats; cuts: Cut[] } | { topic: string; status: "insufficient_data" };

export interface EmployerList {
  snapshot: Snapshot | null;
  employers: string[];
  page: number;
  limit: number;
  total: number;
}

export interface Employer {
  snapshot: Snapshot;
  employerRef: string;
  respondentsBand: string;
  topics: Topic[];
}

type Obj = Record<string, unknown>;
const isObj = (v: unknown): v is Obj => typeof v === "object" && v !== null && !Array.isArray(v);
const isStr = (v: unknown): v is string => typeof v === "string" && v.length > 0;
const isNum = (v: unknown): v is number => typeof v === "number" && Number.isFinite(v);
const isCount = (v: unknown): v is number => isNum(v) && Number.isInteger(v) && v >= 0;
const oneOf = <T extends string>(v: unknown, allowed: readonly T[]): v is T => typeof v === "string" && (allowed as readonly string[]).includes(v);

function readSnapshot(v: unknown): Snapshot | null {
  if (!isObj(v)) return null;
  const { generatedAt, publicationIntervalHours, rulesVersion, minimumGroupSize, deletionsAppearAtNextPublication } = v;
  if (!isStr(generatedAt) || Number.isNaN(Date.parse(generatedAt))) return null;
  if (!isCount(publicationIntervalHours) || !isStr(rulesVersion) || !isCount(minimumGroupSize)) return null;
  if (typeof deletionsAppearAtNextPublication !== "boolean") return null;
  return { generatedAt, publicationIntervalHours, rulesVersion, minimumGroupSize, deletionsAppearAtNextPublication };
}

function readGroups(v: unknown): Group[] | null | undefined {
  if (v === null || v === undefined) return null;
  if (!Array.isArray(v)) return undefined;
  const out: Group[] = [];
  for (const g of v) {
    if (!isObj(g) || !isStr(g.key) || !isCount(g.count)) return undefined;
    out.push({ key: g.key, count: g.count });
  }
  return out;
}

function readStats(v: unknown): Stats | null {
  if (!isObj(v) || !isObj(v.interval)) return null;
  const { n, mean, interval, reliability, coverage } = v;
  const i = interval as Obj;
  if (!isCount(n) || !isNum(mean) || !isNum(i.lower) || !isNum(i.upper) || !isNum(i.level)) return null;
  if (!oneOf(reliability, ["low", "moderate", "high"] as const) || !oneOf(coverage, ["low", "medium", "high"] as const)) return null;
  const distribution = readGroups(v.distribution);
  const verification = readGroups(v.verification);
  if (distribution === undefined || verification === undefined) return null;
  return { n, mean, lower: i.lower, upper: i.upper, level: i.level, reliability, coverage, distribution, verification };
}

function readCell(v: unknown): Cell | null {
  if (!isObj(v) || !isStr(v.band)) return null;
  if (v.status === "ok") {
    const stats = readStats(v.stats);
    return stats ? { band: v.band, status: "ok", stats } : null;
  }
  if (v.status === "none" || v.status === "suppressed") return { band: v.band, status: v.status };
  return null;
}

function readCut(v: unknown): Cut | null {
  if (!isObj(v) || !isStr(v.dimension)) return null;
  // A withheld cut is withheld whole: whatever cells a response carried are not read, so no view can list them.
  if (v.status === "suppressed") return { dimension: v.dimension, status: "suppressed" };
  if (v.status !== "published" || !Array.isArray(v.cells)) return null;
  const cells: Cell[] = [];
  for (const c of v.cells) {
    const cell = readCell(c);
    if (!cell) return null;
    cells.push(cell);
  }
  return { dimension: v.dimension, status: "published", cells };
}

function readTopic(v: unknown): Topic | null {
  if (!isObj(v) || !isStr(v.topic)) return null;
  // No numbers survive for a topic without enough data, whatever the response carried.
  if (v.status === "insufficient_data") return { topic: v.topic, status: "insufficient_data" };
  if (v.status !== "ok" || !Array.isArray(v.cuts)) return null;
  const overall = readStats(v.overall);
  if (!overall) return null;
  const cuts: Cut[] = [];
  for (const c of v.cuts) {
    const cut = readCut(c);
    if (!cut) return null;
    cuts.push(cut);
  }
  return { topic: v.topic, status: "ok", overall, cuts };
}

/** `GET /api/v1/signals/employers`. Anything that is not exactly that shape is not rendered. */
export function readEmployerList(body: unknown): EmployerList | null {
  if (!isObj(body) || !Array.isArray(body.employers)) return null;
  const { page, limit, total } = body;
  if (!isCount(page) || !isCount(limit) || !isCount(total)) return null;
  const snapshot = body.snapshot === null ? null : readSnapshot(body.snapshot);
  if (body.snapshot !== null && snapshot === null) return null;
  const employers: string[] = [];
  for (const e of body.employers) {
    if (!isStr(e)) return null;
    employers.push(e);
  }
  return { snapshot, employers, page, limit, total };
}

/** `GET /api/v1/signals/employers/{ref}`. */
export function readEmployer(body: unknown): Employer | null {
  if (!isObj(body) || !Array.isArray(body.topics)) return null;
  const snapshot = readSnapshot(body.snapshot);
  if (!snapshot || !isStr(body.employerRef) || !isStr(body.respondentsBand)) return null;
  const topics: Topic[] = [];
  for (const t of body.topics) {
    const topic = readTopic(t);
    if (!topic) return null;
    topics.push(topic);
  }
  return { snapshot, employerRef: body.employerRef, respondentsBand: body.respondentsBand, topics };
}

export type SignalsFailure =
  | { kind: "not_found" }
  | { kind: "rate_limited"; retryAfter: number }
  | { kind: "unauthenticated" }
  | { kind: "consent_required" }
  | { kind: "unavailable" }
  | { kind: "generic" };

/** What to wait when the service gave no usable `Retry-After`: a calm default, never zero. */
export const DEFAULT_WAIT_SECONDS = 30;
const MAX_WAIT_SECONDS = 3600;

/**
 * Maps a non-2xx answer to what the page shows. 404 SIGNALS_EMPLOYER_NOT_FOUND and 400 SIGNALS_INVALID_EMPLOYER_REF are the
 * same state, "nothing to show": the service answers identically for an unknown employer and one below the minimum group
 * size, and the page must not be a second oracle (AGGREGATION R10). The wait of a 429 comes from `Retry-After` (seconds).
 */
export function signalsFailure(status: number, body: unknown, retryAfterHeader: string | null): SignalsFailure {
  const b = isObj(body) ? body : {};
  if (status === 404 && b.code === "SIGNALS_EMPLOYER_NOT_FOUND") return { kind: "not_found" };
  if (status === 400 && b.code === "SIGNALS_INVALID_EMPLOYER_REF") return { kind: "not_found" };
  if (status === 429) {
    const header = retryAfterHeader && /^\d{1,6}$/.test(retryAfterHeader.trim()) ? Number(retryAfterHeader.trim()) : undefined;
    return { kind: "rate_limited", retryAfter: header && header > 0 ? Math.min(header, MAX_WAIT_SECONDS) : DEFAULT_WAIT_SECONDS };
  }
  if (status === 401) return { kind: "unauthenticated" };
  if (status === 403 && b.error === "consent_required") return { kind: "consent_required" };
  if (status === 502 || status === 503 || status === 504) return { kind: "unavailable" };
  return { kind: "generic" };
}

/** `{name}` placeholders of a catalog template. A template and its values are both ours; nothing from the API is a template. */
export function fill(template: string, values: Record<string, string | number>): string {
  return template.replace(/\{(\w+)\}/g, (whole, name: string) => (name in values ? String(values[name]) : whole));
}

/** Two decimals, as the API sends them (mean to two decimals, the interval rounded outwards to two). Not a rounding policy: a rendering. */
export const twoDecimals = (value: number): string => value.toFixed(2);

/** "95" for a level of 0.95. */
export const levelPercent = (level: number): string => String(Math.round(level * 100));

/** The batch start in UTC, never finer than the API's own coarse time. */
export function formatBatchStart(iso: string): string {
  return new Intl.DateTimeFormat("en", {
    year: "numeric", month: "long", day: "numeric", hour: "2-digit", minute: "2-digit", hourCycle: "h23", timeZone: "UTC", timeZoneName: "short",
  }).format(new Date(iso));
}

export const PAGE_SIZE = 20;

/** A `?page=` value as a positive integer; anything else is page 1. */
export function readPage(value: string | string[] | undefined): number {
  const v = Array.isArray(value) ? value[0] : value;
  return v && /^[1-9]\d{0,5}$/.test(v) ? Number(v) : 1;
}
