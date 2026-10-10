// The interview contract as the BFF and the page see it: a 1:1 mirror of plan section 10 (docs/architecture/web-app-plan.md), the
// one shape both sides must agree on. It changes only together with that section, in the same pull request. The service side
// is ExitInterviewAgent.Contracts (InterviewContracts.cs). Parsers below check the shape of an answer before the page uses it:
// anything unexpected becomes `null`, which the client reports as a generic failure instead of rendering half of a record.

export const INTERVIEW_LANGUAGES = ["pl", "en"] as const;
export type InterviewLanguage = (typeof INTERVIEW_LANGUAGES)[number];

export const TENURES = ["lt_6m", "6m_1y", "1y_3y", "3y_5y", "5y_10y", "gt_10y"] as const;
export type Tenure = (typeof TENURES)[number];

/** `awaiting_consent`, `in_progress`, `completed`, `stopped` (nothing kept) or `failed` (service fault; the credit is returned). */
export const INTERVIEW_STATUSES = ["awaiting_consent", "in_progress", "completed", "stopped", "failed"] as const;
export type InterviewStatus = (typeof INTERVIEW_STATUSES)[number];

export const TURN_KINDS = ["opening", "consent_reask", "topic", "probe", "clarification", "deep_probe", "redirect", "close", "stop"] as const;
export type TurnKind = (typeof TURN_KINDS)[number];

/** The tile kinds the contract carries (TileKind in the agent; the CLI renders more, the service sends these six). */
export const TILE_KINDS = ["glassdoor", "google_review", "reddit", "short_note", "overview", "facts"] as const;
export type TileKind = (typeof TILE_KINDS)[number];

/** A reply's length bounds, the same as the contract's `text` (1..2000 characters). */
export const MAX_REPLY_CHARS = 2000;

/**
 * Every stable `code` the service answers with (ExitInterviewAgent.Contracts `InterviewCodes` and `BillingCodes`, and the kernel's
 * `rate_limited`). The same list is in tests/contracts/codes.json; interview-contract.test.ts keeps the two equal.
 */
export const CONTRACT_ERROR_CODES = [
  "bad_signature",
  "billing_disabled",
  "email_not_verified",
  "gone",
  "interview_ended",
  "interview_in_progress",
  "interviews_disabled",
  "invalid_request",
  "not_completed",
  "not_found",
  "payment_required",
  "provider_unavailable",
  "rate_limited",
  "reply_in_progress",
  "reply_invalid",
  "request_cancelled",
] as const;

export interface Turn {
  index: number;
  kind: TurnKind;
  text: string;
}

export interface Ending {
  reason: string;
}

/** `POST /interviews` answer: the opening turn. */
export interface InterviewStarted {
  id: string;
  status: InterviewStatus;
  language: InterviewLanguage;
  expiresAt: string;
  turn: Turn;
}

/** `POST /interviews/{id}/reply` answer. `turn` is absent when the reply produced none; `ending` is on the last turn. */
export interface ReplyResult {
  status: InterviewStatus;
  turn?: Turn;
  ending?: Ending;
}

/** `GET /interviews/{id}` answer. */
export interface InterviewState {
  id: string;
  status: InterviewStatus;
  language: InterviewLanguage;
  turnCount: number;
  expiresAt: string;
}

export interface Tile {
  kind: TileKind;
  text: string;
}

export interface Tiles {
  items: Tile[];
  dropped: { code: string }[];
  /** The same legal notice the CLI prints, in the interview's language. */
  notice: string;
}

export type TopicKey = "onboarding" | "management" | "growth" | "pay_vs_promises" | "culture" | "reason_for_leaving";
export const TOPIC_KEYS = ["onboarding", "management", "growth", "pay_vs_promises", "culture", "reason_for_leaving"] as const;

/** One topic of the record (`#/$defs/topic` in schemas/exit-interview-record.v1.schema.json). */
export interface RecordTopic {
  status: "no_data" | "covered";
  rating: number | null;
  confidence: "low" | "medium" | "high" | null;
  quotes: string[];
}

/** The interview record, as far as the page reads it (schemas/exit-interview-record.v1.schema.json). Shown readably, never as raw JSON. */
export interface InterviewRecord {
  schemaVersion: string;
  interviewId: string;
  piiMasked: boolean;
  context: { tenureBand: string; seniorityBand?: string; functionBand?: string };
  interview: { protocolVersion: string; language: string; aiDisclosed: boolean; durationBand: string; turnBand: string };
  topics: Record<TopicKey, RecordTopic>;
}

/** Counts only (plan section 10): no text in usage. */
export interface Usage {
  modelCalls: number;
  tokensEstimated: number;
}

/** `GET /interviews/{id}/result` answer, once `status` is `completed`. */
export interface InterviewResult {
  record: InterviewRecord;
  tiles: Tiles;
  usage: Usage;
}

export interface Credits {
  balance: number;
}

export interface Checkout {
  url: string;
}

// --- parsers ---------------------------------------------------------------------------------------------------------------

type Obj = Record<string, unknown>;
const isObj = (v: unknown): v is Obj => typeof v === "object" && v !== null && !Array.isArray(v);
const isStr = (v: unknown): v is string => typeof v === "string";
const isInt = (v: unknown): v is number => typeof v === "number" && Number.isInteger(v) && v >= 0;
const oneOf = <T extends string>(list: readonly T[], v: unknown): v is T => typeof v === "string" && (list as readonly string[]).includes(v);

export function parseTurn(v: unknown): Turn | null {
  if (!isObj(v) || !isInt(v.index) || !oneOf(TURN_KINDS, v.kind) || !isStr(v.text)) return null;
  return { index: v.index, kind: v.kind, text: v.text };
}

export function parseStarted(v: unknown): InterviewStarted | null {
  if (!isObj(v) || !isStr(v.id) || !oneOf(INTERVIEW_STATUSES, v.status) || !oneOf(INTERVIEW_LANGUAGES, v.language) || !isStr(v.expiresAt)) return null;
  const turn = parseTurn(v.turn);
  return turn ? { id: v.id, status: v.status, language: v.language, expiresAt: v.expiresAt, turn } : null;
}

/** The service writes `"turn": null` and `"ending": null` on an ordinary reply (the field is present, the value is null): both mean absent. */
export function parseReply(v: unknown): ReplyResult | null {
  if (!isObj(v) || !oneOf(INTERVIEW_STATUSES, v.status)) return null;
  const out: ReplyResult = { status: v.status };
  if (v.turn !== undefined && v.turn !== null) {
    const turn = parseTurn(v.turn);
    if (!turn) return null;
    out.turn = turn;
  }
  if (v.ending !== undefined && v.ending !== null) {
    if (!isObj(v.ending) || !isStr(v.ending.reason)) return null;
    out.ending = { reason: v.ending.reason };
  }
  return out;
}

export function parseState(v: unknown): InterviewState | null {
  if (!isObj(v) || !isStr(v.id) || !oneOf(INTERVIEW_STATUSES, v.status) || !oneOf(INTERVIEW_LANGUAGES, v.language)) return null;
  if (!isInt(v.turnCount) || !isStr(v.expiresAt)) return null;
  return { id: v.id, status: v.status, language: v.language, turnCount: v.turnCount, expiresAt: v.expiresAt };
}

function parseTopic(v: unknown): RecordTopic | null {
  if (!isObj(v) || !oneOf(["no_data", "covered"] as const, v.status)) return null;
  const rating = v.rating === null || (typeof v.rating === "number" && Number.isInteger(v.rating)) ? (v.rating as number | null) : undefined;
  const confidence = v.confidence === null || oneOf(["low", "medium", "high"] as const, v.confidence) ? (v.confidence as RecordTopic["confidence"]) : undefined;
  if (rating === undefined || confidence === undefined || !Array.isArray(v.quotes) || !v.quotes.every(isStr)) return null;
  return { status: v.status, rating, confidence, quotes: v.quotes };
}

function parseRecord(v: unknown): InterviewRecord | null {
  if (!isObj(v) || !isStr(v.schemaVersion) || !isStr(v.interviewId) || typeof v.piiMasked !== "boolean") return null;
  if (!isObj(v.context) || !isStr(v.context.tenureBand)) return null;
  if (!isObj(v.interview) || typeof v.interview.aiDisclosed !== "boolean" || !isStr(v.interview.language)) return null;
  if (!isObj(v.topics)) return null;
  const topics = {} as Record<TopicKey, RecordTopic>;
  for (const key of TOPIC_KEYS) {
    const topic = parseTopic(v.topics[key]);
    if (!topic) return null;
    topics[key] = topic;
  }
  const i = v.interview;
  return {
    schemaVersion: v.schemaVersion,
    interviewId: v.interviewId,
    piiMasked: v.piiMasked,
    context: v.context as InterviewRecord["context"],
    interview: {
      protocolVersion: isStr(i.protocolVersion) ? i.protocolVersion : "",
      language: i.language as string,
      aiDisclosed: i.aiDisclosed === true, // checked above: a boolean or the answer was refused
      durationBand: isStr(i.durationBand) ? i.durationBand : "",
      turnBand: isStr(i.turnBand) ? i.turnBand : "",
    },
    topics,
  };
}

export function parseResult(v: unknown): InterviewResult | null {
  if (!isObj(v) || !isObj(v.tiles) || !isObj(v.usage)) return null;
  const record = parseRecord(v.record);
  if (!record) return null;
  const { items, dropped, notice } = v.tiles;
  if (!Array.isArray(items) || !Array.isArray(dropped) || !isStr(notice)) return null;
  const parsedItems: Tile[] = [];
  for (const item of items) {
    if (!isObj(item) || !oneOf(TILE_KINDS, item.kind) || !isStr(item.text)) return null;
    parsedItems.push({ kind: item.kind, text: item.text });
  }
  if (!dropped.every((d) => isObj(d) && isStr(d.code))) return null;
  if (!isInt(v.usage.modelCalls) || !isInt(v.usage.tokensEstimated)) return null;
  return {
    record,
    tiles: { items: parsedItems, dropped: dropped as { code: string }[], notice },
    usage: { modelCalls: v.usage.modelCalls, tokensEstimated: v.usage.tokensEstimated },
  };
}

export function parseCredits(v: unknown): Credits | null {
  return isObj(v) && isInt(v.balance) ? { balance: v.balance } : null;
}

export function parseCheckout(v: unknown): Checkout | null {
  return isObj(v) && isStr(v.url) ? { url: v.url } : null;
}
