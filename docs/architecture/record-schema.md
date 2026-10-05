# The exit-interview record, schema v1

The record is the contract of the whole system: the CLI and the MCP path produce it, the ingest service validates and
stores it, the signals module aggregates it, the evaluation harness checks its fidelity. This page is the field-by-field
rationale, the privacy reasoning and the versioning policy.

- Schema (draft 2020-12): [`schemas/exit-interview-record.v1.schema.json`](../../schemas/exit-interview-record.v1.schema.json),
  `$id` `urn:exit-interview-agent:schema:exit-interview-record:v1`.
- Library: `src/ExitInterviewAgent.Records` (immutable model, validation, canonical serialization, quote fidelity).
- Decisions: [ADR-0007](../adr/0007-record-context-bands.md) (bands), [ADR-0008](../adr/0008-validation-library-and-json-schema-package.md)
  (validation), [ADR-0009](../adr/0009-record-schema-versioning.md) (versioning), [ADR-0011](../adr/0011-no-per-person-identifier-in-the-record.md)
  (no per-person identifier).

## What a record is, and is not

A record is **pseudonymous, not anonymous** ([ADR-0018](../adr/0018-records-are-treated-as-personal-data.md)). It holds structured judgements about six topics, the verbatim excerpts that
support them, and coarse context. It holds nothing that identifies the person or links to their account. It can still
describe someone who is the only person in a band at a small employer, and its quotes are free text; the sections below
say what is done about both and what remains.

An example (shortened; the full golden records are in `tests/ExitInterviewAgent.Records.Tests/Fixtures/valid/`):

```json
{
  "schemaVersion": "1",
  "interviewId": "0f3c9a1e7b2d4c58a6e1903fd2b47c11",
  "employerRef": "acme-sp-zoo",
  "context": { "tenureBand": "1y_3y", "seniorityBand": "mid", "functionBand": "engineering" },
  "topics": {
    "onboarding": { "status": "covered", "rating": 2, "confidence": "medium", "quotes": ["..."] },
    "management": { "status": "no_data", "rating": null, "confidence": null, "quotes": [] }
  },
  "piiMasked": true,
  "interview": { "protocolVersion": "1.0", "language": "en", "durationBand": "20m_40m", "turnBand": "20_40" }
}
```

(`topics` always holds all six keys; two are shown.)

## Fields

| Field | Type and constraint | Why |
|---|---|---|
| `schemaVersion` | the string `"1"` | Selects the contract. Major only; see [Versioning](#versioning). |
| `interviewId` | 32 lowercase hex characters | 128 random bits from the OS CSPRNG, derived from nothing (not from the account, employer, time or content). Lets a record be referred to (receipt, dedupe, eval join) without naming anyone. |
| `employerRef` | 3-64 chars, `^[a-z0-9]+(-[a-z0-9]+)*$` | An **opaque** reference to an employer. Only the reference is defined here; the registry that issues it is out of scope. The strict pattern keeps free text, names and emails out of it. |
| `context.tenureBand` | enum, required | One of six bands. See [ADR-0007](../adr/0007-record-context-bands.md). |
| `context.seniorityBand` | enum, optional | Four bands. Omitted, never null, when withheld. |
| `context.functionBand` | enum, optional | Six broad bands. Omitted, never null, when withheld. |
| `topics.<topic>` | object, all six required | `onboarding`, `management`, `growth`, `pay_vs_promises`, `culture`, `reason_for_leaving`. |
| `topics.<topic>.status` | `no_data` \| `covered` | **No data is not a low rating.** `no_data` = not discussed, declined, or not answerable. |
| `topics.<topic>.rating` | integer 1-5 or `null` | 1 very negative, 5 very positive. `null` for `no_data`, or for a covered topic that was discussed but is not rateable. |
| `topics.<topic>.confidence` | `low` \| `medium` \| `high`, or `null` for `no_data` | How well the interview supports the rating. Three levels, not a number: a score with decimals would claim precision the extraction does not have. |
| `topics.<topic>.quotes` | up to 5 strings of up to 400 code points | Verbatim excerpts supporting the rating. `no_data`: none. `covered`: at least one. Inert data (below). |
| `piiMasked` | boolean | True when the quotes came from a transcript that went through the PII masker. Ingest rejects `false` by default. |
| `interview.protocolVersion` | `^[0-9]{1,3}\.[0-9]{1,3}$` | Which interview protocol the agent followed, so quality can be compared by protocol. |
| `interview.language` | `^[a-z]{2,3}$` | ISO 639 language code of the interview. |
| `interview.aiDisclosed` | boolean | True when the interviewer told the interviewee it is an AI before the interview began. Ingest rejects `false` by default (`RecordLimits.RequireAiDisclosed`). Recorded so the disclosure can be audited by the eval harness. |
| `interview.durationBand`, `interview.turnBand` | enums, four values each | Coarse shape of the session. Bands, not exact numbers: an exact duration or turn count is a fingerprint of the session. |

### Topic semantics (enforced by the schema)

| `status` | `rating` | `confidence` | `quotes` |
|---|---|---|---|
| `no_data` | `null` | `null` | `[]` |
| `covered` | 1-5 or `null` | `low`/`medium`/`high` | 1 to 5 |

A rating is never present without a supporting quote. A violation reports `TOPIC_INCONSISTENT` at the offending
member. Aggregation must count `no_data` as missing, never as a low score; that is why it is a distinct state.

## Privacy reasoning

0. **No emotion, sentiment or affect field.** Ratings are about topics, never inferences about the person's feelings; the same
   architecture test that forbids identifiers rejects such names ([ADR-0011](../adr/0011-no-per-person-identifier-in-the-record.md)).
1. **No per-person identifier, anywhere.** No user id, account id, email, IP address, name, device or session id, and no
   timestamp (a timestamp is a join key to access logs; the privacy design allows at most an ISO-week bucket and this schema
   carries none, which is stricter). The only identifier is the random `interviewId`. This is enforced,
   not promised: architecture tests fail the build if a schema property or model member resembles such an identifier, if any
   schema object is open to extra properties, or if a context field is not an enum
   ([ADR-0011](../adr/0011-no-per-person-identifier-in-the-record.md)).
2. **Closed objects.** Every object has `additionalProperties: false`, so an unlisted field cannot ride along. Errors for an
   unknown key report the parent path, never the key (the key could be an email address).
3. **Context is coarse bands.** Why these bands, what is deliberately absent and what the signals module must do about small
   groups: [ADR-0007](../adr/0007-record-context-bands.md).
4. **Quotes are the residual risk.** They are free text taken from a transcript. They are limited (5 per topic, 400 code
   points each, no control or bidirectional-override characters), taken from a masked transcript, and checked for fidelity
   (below). Masking is heuristic and has measured limits: [`../privacy/pii-detector.md`](../privacy/pii-detector.md). A quote
   can still identify someone by description ("the only woman in the Gdansk office"); the record cannot fix that, and the
   threat model must carry it as a residual risk.
5. **Quotes are inert data.** Nothing interprets a quote: a `$ref`, a JSON fragment, a template expression or a prompt-injection
   sentence inside a quote is just a string. The validator never dereferences anything, and tests assert that such quotes
   neither change the record's other fields nor survive a round trip as structure. Consumers must keep that discipline:
   encode at render time, never concatenate a quote into a query or a prompt as an instruction.

## Validation

`RecordValidator.Validate(json)` returns a `ValidationOutcome`: either the mapped immutable `InterviewRecord`, or a list of
`RecordError(Code, Path)`. **Errors never contain submitted text**; a path is made only of names the schema defines and
array indexes (anything else becomes `*`). Order of checks: payload size, JSON syntax and depth, duplicate keys, schema, the
`piiMasked` policy.

| Code | Meaning |
|---|---|
| `PAYLOAD_TOO_LARGE` | Over `RecordLimits.MaxPayloadBytes` (default 160 KiB; the worst valid record is about 144 kB). |
| `NOT_JSON` | Not valid UTF-8 JSON. |
| `DUPLICATE_KEY` | An object repeats a property name (ambiguous, rejected). |
| `NESTING_TOO_DEEP` | Deeper than `RecordLimits.MaxDepth` (default 8; a valid record nests 5). |
| `UNSUPPORTED_SCHEMA_VERSION` | `schemaVersion` is not `"1"`. |
| `MISSING_FIELD` | A required property is absent. Path is the object that lacks it. |
| `UNKNOWN_FIELD` | An object has a property the schema does not define. Path is the object. |
| `WRONG_TYPE` | A value has the wrong JSON type. |
| `VALUE_NOT_ALLOWED` | A value is not one of the allowed enum values. |
| `BAD_FORMAT` | A string does not match its pattern (ids, employer reference, quotes with control characters or only white space). |
| `OUT_OF_RANGE` | A number is outside its range (rating not 1-5). |
| `LENGTH_LIMIT` | Too many or too few items, or a string that is too long. |
| `TOPIC_INCONSISTENT` | A topic breaks the `no_data` / `covered` rules above. |
| `AI_NOT_DISCLOSED` | `aiDisclosed` is `false` and the policy requires `true` (`RecordLimits.RequireAiDisclosed`). |
| `PII_NOT_MASKED` | `piiMasked` is `false` and the policy requires `true` (`RecordLimits.RequirePiiMasked`). |
| `VALIDATION_TIMEOUT` | Schema pattern matching exceeded `RecordLimits.RegexTimeout` (denial-of-service guard). |
| `SCHEMA_VIOLATION` | Fallback for a violation without a more specific code. |
| `QUOTE_NOT_VERBATIM` | Reserved for callers that turn a failed `VerifyQuotes` into an error response. |

Codes are part of the contract: renamed or reused only with a new schema major.

### Canonical form

`RecordSerializer.SerializeCanonical` gives the same bytes for the same record: UTF-8, no insignificant white space,
schema property order, optional bands omitted when absent, default System.Text.Json string escaping (non-ASCII as
`\uXXXX`, independent of the consumer's encoder). Round trip (`Validate` then `SerializeCanonical`) is tested for every
golden record.

### Quote fidelity

`QuoteVerifier.VerifyQuotes(transcript, record)` checks that every quote is a verbatim substring of the transcript
**after one normalization, applied to both sides**: every maximal run of Unicode white space becomes one space, and the ends
are trimmed. Nothing else changes: case, punctuation, diacritics and Unicode composition are compared ordinally, so a
paraphrase, a case change, a stripped diacritic or a skipped turn fails. The result lists mismatches by topic and quote
index, never by text. Run it against the **masked** transcript, the one the extractor saw.
**It is client-side and eval-side only: the server never holds the transcript, so it cannot verify quotes.** What the server
can and does check is the schema, the caps and the `piiMasked` and `aiDisclosed` flags; a fabricated quote that passes the schema is
indistinguishable from a real one at ingest (a hostile client can submit anything; see the threat model). Limitation: text that is
canonically equivalent but differently composed (a precomposed "ą" against "a" plus a combining ogonek) is a
mismatch; that fails closed.

## Versioning

- **v1 is immutable once released.** After the release gate the file changes only for the wording of a `description`.
- **Objects are closed**, so a v1 validator rejects a record with a field it does not know.
- **Additive evolution = a new major, as a superset of the old one.** `v2` may add optional properties, widen an enum, raise
  a cap or add an optional topic; every valid v1 record stays valid under v2, so stored records need no migration. Because
  v1 validators reject v2 records, deployments upgrade validators before producers.
- **Breaking = a new major that is not a superset** (remove or rename a field, narrow an enum, lower a cap, change a type,
  make an optional field required, change what `no_data` means). It needs an ADR, a migration note and a deployment order.
- **No version of the schema may contain a per-person identifier.**
- The library embeds the schema from the published file and a test pins them byte for byte; enum wire names and the numeric
  caps of the model are pinned to the schema by tests.

Full reasoning: [ADR-0009](../adr/0009-record-schema-versioning.md).

## Using the library

```csharp
var validator = new RecordValidator();                       // share one instance; it is thread-safe
ValidationOutcome outcome = validator.Validate(bodyBytes);
if (!outcome.IsValid) return Problem(outcome.Errors);        // codes and paths only
InterviewRecord record = outcome.Record!;

QuoteVerification fidelity = QuoteVerifier.VerifyQuotes(maskedTranscript, record);
byte[] stored = RecordSerializer.SerializeCanonical(record);
var id = InterviewId.NewRandom();                            // for a new record
```

## Open problems

- The signals module must apply its minimum-count threshold to every published cut, including band breakdowns
  ([ADR-0007](../adr/0007-record-context-bands.md)); the schema cannot enforce it.
- `employerRef` has no registry; until one exists, two references can name one employer.
- Quote fidelity compares code points; whether real model output needs Unicode normalization first is not yet measured.
