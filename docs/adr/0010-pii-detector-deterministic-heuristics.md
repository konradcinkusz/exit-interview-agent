# 0010. PII detector: deterministic rules, kinds and offsets only, fail-closed option

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): brief §6 ("never ask for or store names of individuals (detect and mask)"; no PII in logs or traces), `ai-evals` (a deterministic layer under any model judgement), `metric-ethics` §3 (no number without its confidence)

## Context

The agent must keep names of individuals and contact data out of records, and the ingest service must not rely on a
client's word that it did so. A model-based detector would add cost, latency, a network dependency and an
unreproducible result, and sending text to a model to find PII is itself a disclosure.

## Decision

- `ExitInterviewAgent.Privacy` is BCL only: regular expressions with a bounded match time, checksums (PESEL, NIP),
  and language heuristics. No model, no network, no I/O, no dependency on other repository projects. Same input and
  options, same output.
- **Findings carry kind and UTF-16 offsets only** (and a basis: pattern, heuristic, fail-closed). No finding member holds
  text, so a finding can be logged or traced without leaking the thing it points at. A test asserts this by reflection.
- **Names** are the hard case, covered by cues rather than by a dictionary of people: titles (English and Polish),
  relation constructions ("my manager X", "moja szefowa X", "X, my manager", "reported to X"), runs of two or more
  capitalised tokens, initials, a small given-name lexicon with Polish inflection, and Polish surname shapes.
  A configurable allow-list keeps employer and product names from being taken for people; it applies to names only,
  never to emails or URLs.
- **Overlaps are merged into their union**, so no fragment of any candidate is left unmasked. Placeholders are
  `[EMAIL]`, `[PHONE]`, `[URL]`, `[HANDLE]`, `[IP_ADDRESS]`, `[NATIONAL_ID]`, `[EMPLOYEE_ID]`, `[PERSON]`; masking
  is idempotent.
- **Fail-closed option** (`PiiOptions.FailClosed`): when uncertain, mask. It adds unrecognised capitalised words mid-sentence,
  ambiguous given names at the start of a sentence, and 7-8 digit runs. It never masks less than the default.
- **Quantifiers are bounded** (an early version hit quadratic time on long unbroken tokens; the pathological-input tests exist
  because of it).
- **Honest limits** are documented with measured numbers and their confidence intervals in `docs/privacy/pii-detector.md`.
  A detector of this kind has no recall guarantee; the design assumes it is one layer, with the user's own review
  (CLI) and the host model's instructions (MCP) in front of it, and quote-length caps behind it.

## Consequences

- Misses are expected for unknown given names used alone mid-sentence, names in languages other than English and Polish,
  lowercase names, nicknames and descriptions that identify a person without naming them ("the only woman in the
  Gdansk office"). These are listed, not hidden.
- Adding a language means adding cues, a lexicon and corpus lines; the evaluation command is the acceptance test.
- Trigger to revisit: a measured held-out recall under the stated floor, or evidence from simulated interviews (T4/T7)
  that real model output leaks names the corpus does not model.
