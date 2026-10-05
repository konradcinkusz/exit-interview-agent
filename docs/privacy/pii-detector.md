# The PII detector and masker

`src/ExitInterviewAgent.Privacy` finds and masks personal data in interview text before it is turned into a record. It is
deterministic (rules, checksums and language heuristics), uses no model and no network, and depends only on the framework.
Decision records: [ADR-0010](../adr/0010-pii-detector-deterministic-heuristics.md), [ADR-0063](../adr/0063-pii-detector-rule-cost-and-over-masking.md) (rule cost, over-masking).

**Read this first: it is a heuristic with measured limits, not a guarantee.** It reduces how often a name or a phone number
reaches a record; it does not make leaking one impossible. It is one layer (the interview agent is instructed never to ask
for names; the person can review in the CLI; quotes are length-capped), and no part of the design relies on it being perfect.

## What it detects

| Kind | Placeholder | Rule |
|---|---|---|
| Email | `[EMAIL]` | Standard addresses (Unicode letters allowed), and the obfuscated `name[at]host[dot]tld` form. |
| URL | `[URL]` | `http(s)://`, `ftp://`, `www.`, and bare hosts on a list of common TLDs (`linkedin.com/in/...`). Technology names such as `ASP.NET` or `Next.js` are excluded. |
| IP address | `[IP_ADDRESS]` | IPv4 and full-form IPv6. A number after "version", "build" and similar is not an address. |
| Handle | `[HANDLE]` | `@name`, and `linkedin:` / `github/` / `telegram:` style identifiers (the identifier part). |
| National id | `[NATIONAL_ID]` | PESEL (11 digits, checksum), NIP (10 digits, checksum or dashed form), keyworded REGON/KRS/NIP/PESEL, Polish ID card (`ABC 123456`) and passport, US SSN, UK NINO, IBAN and Polish 26-digit account numbers. Currency codes (`PLN 150000`) are not ID cards. An 11-digit or 10-digit run is masked even when its checksum fails (a mistyped id is still an id). |
| Employee id | `[EMPLOYEE_ID]` | Labelled (`employee ID`, `staff no.`, `numer pracownika`, `badge ...`) and bare `EMP-`, `EID-`, `PRC-`, `ID-` forms. |
| Phone | `[PHONE]` | 9-15 digits with single separators, `+`/`00` prefixes and parentheses, and a number after `tel`/`phone`/`zadzwoń`/`numer`. Dates, salary ranges (`10 000 - 12 000`) and thousands-grouped numbers (`1 450 000 000`) are not phone numbers. |
| Person name | `[PERSON]` | See below. |

### Names of individuals

Names are found by cues, not by a list of people (English and Polish):

1. **Titles**: Mr, Mrs, Ms, Dr, Prof, Sir, Pan, Pani, mgr, inz., `p.` followed by a name. The title stays in the text; the name is masked.
2. **Relation constructions**: "my manager X", "our director X", "my boss, X", "X, my manager", "reported (directly) to X",
   "worked under X", "named / called X", and Polish "mój szef X", "moja przełożona, X", "z moim kierownikiem X" (inflected forms of the
   relation noun are matched).
3. **Runs of two or more capitalised tokens** ("Firstname Lastname", particles such as `van der` allowed, hyphenated surnames).
4. **Initials** ("J. Brandt").
5. **A given-name lexicon** (162 English and 127 Polish base given names, counted from `Lexicon.cs`, with Polish case endings generated) for a single token.
6. **Polish surname shapes** (`-ski/-ska/-cki/-dzki/-wicz/-owski` and their case forms) for a single token away from a sentence start.

Words that are capitalised but not names are screened out by a stop list (days, months, nationalities, common places, tools,
role words, speaker labels such as `Interviewer:` and `User:`, sentence openers such as "Ask"), and by the **allow-list**: pass the
employer and product names (`PiiOptions.AllowList`, case-insensitive, a short ending allowed for entries of five or more
characters) so that "Zephyrix Orbit Suite" is not taken for a person. The allow-list never unmasks an email or a URL.

### What a finding is

`PiiFinding(Kind, Start, Length, Basis)`: the kind, UTF-16 offsets into the **original** text, and why it was flagged
(`Pattern`, `Heuristic` or `FailClosed`). **It carries no text**, so it can be logged or traced without leaking what it points at
(a test asserts there is no string member). Overlapping candidates are merged into their union, so no fragment is left
unmasked; masking is idempotent.

### Fail-closed

`new PiiOptions { FailClosed = true }` masks when uncertain: unrecognised capitalised words in mid-sentence, ambiguous given names
("Mark", "Will") at the start of a sentence, and 7-8 digit runs. It never masks less than the default (tested). It costs
precision (see the table). Recommended wherever a leaked name is worse than a damaged quote, which is every place this repository
uses it.

## What it cannot do (known limits)

- **Unknown given names used alone** in the middle of a sentence ("thanks to Aisha"): only fail-closed catches them.
- **Languages other than English and Polish.** Capitalisation rules differ (German capitalises every noun; Czech, Slovak and
  Ukrainian have other name forms). No cues exist for them.
- **Lowercase names, nicknames and transliterations**, names in a different script, and names inside a longer capitalised phrase
  that the stop list swallows.
- **Identification without a name**: "the only woman in the Gdansk office", "the CFO who left in March", a distinctive story.
  No pattern detector can find these; they are a residual risk of the free-text quotes.
- **Ambiguous words**: "Will", "Mark" and employer names that look like people. The allow-list helps only for names you list.
- **Locations and organisations** are not detected (a street address is not masked; a company name is deliberately not).
- **Unusual number formats** (a phone number written in words, an id with spaces in unusual places).
- **Topic words in fail-closed mode.** A capitalised topic noun ("Pay", "Culture", "Benefits" and their Polish forms) is not a name and is left alone, as is the
  first word after a closing transcript tag; a topic that is not on the stop list is still masked in fail-closed mode
  ([OP-30](../OPEN-PROBLEMS.md#op-30-fail-closed-over-masking-of-capitalised-topic-words-is-bounded-only-by-a-list)).
- **False positives**: capitalised product or project names that are not on the allow-list ("Project Phoenix") can be masked as a
  person; in fail-closed mode every unknown capitalised mid-sentence word can.
- **Adversarial text.** Patterns have bounded quantifiers and a match timeout (2 s per rule), and pathological inputs are tested, but a
  caller must treat an exception as "do not submit". Cost per rule was measured on 200 000-character adversarial inputs
  (`--filter "Category=RuleCost"`): none is superlinear; the obfuscated-email scan, formerly about 7 microseconds per character on a long
  unbroken token, is now found from its marker and costs about 350 nanoseconds per character at worst ([ADR-0063](../adr/0063-pii-detector-rule-cost-and-over-masking.md)).
- **Obfuscated emails** are recognised in the `[at]`, `(at)` and `{at}` forms with `.`, `[dot]` or `(dot)` before the domain parts; other spellings are not
  ([OP-31](../OPEN-PROBLEMS.md#op-31-obfuscated-email-spellings-beyond-the-bracketed-forms-are-not-detected)).

## Measured numbers

Corpus: synthetic only, no real people, numbers or addresses, English and Polish, in
`tests/ExitInterviewAgent.Privacy.Tests/Corpus/`:

| File | Sample lines | Gold PII spans | Purpose |
|---|---|---|---|
| `dev.txt` | 101 | 68 | Rules were tuned against it. Every rule's cases are here, so its numbers are an upper bound. |
| `heldout.txt` | 65 | 49 | Written before the detector was first run on it, and no rule was tuned on its results. |

The held-out file is **not independent evidence**: the same author wrote the rules and the corpus, and the two share assumptions.
Treat its numbers as an optimistic estimate and as a regression guard, not as a prediction of performance on real transcripts.
After the first held-out run (recall 95.9%, precision 97.9% in default mode) the detector received fixes motivated by unit-test
failures (greedy keyword matching, titles read as names, bounded quantifiers); the numbers below are from the final detector.
The corpus is small (49 spans), so the intervals are wide; they are 95% Wilson score intervals.

Definitions. A gold span is **caught** when findings cover at least half of its characters and **fully masked** when they cover all of
them (anything less leaves part of the data in the text). A finding is a **true positive** when it overlaps a gold span by at least
one character, otherwise a **false positive**. The kind is not required to agree; what matters is that the data is masked.

| Run | Gold spans | Caught (recall) | Fully masked | Findings | False positives | Precision |
|---|---|---|---|---|---|---|
| dev, default | 68 | 68 (100.0%, CI 95-100%) | 68 | 68 | 0 | 100.0% (CI 95-100%) |
| dev, fail-closed | 68 | 68 (100.0%, CI 95-100%) | 68 | 74 | 6 | 91.9% (CI 83-96%) |
| held-out, default | 49 | 47 (95.9%, CI 86-99%) | 47 | 47 | 0 | 100.0% (CI 92-100%) |
| held-out, fail-closed | 49 | 49 (100.0%, CI 93-100%) | 49 | 52 | 3 | 94.2% (CI 84-98%) |

Held-out recall by kind (default mode): email 3/3, employee id 2/2, handle 2/2, national id 3/3, IP 1/1, **person 29/31**, phone 4/4,
URL 3/3. The two misses are the two unknown given names in "Thanks to Aisha and Dmitri": fail-closed catches both. The fail-closed
false positives on the held-out file are two short digit runs (a ticket number and an order number) and one capitalised word (an inflected tool name, "Excelu"); on the
dev file they are capitalised tool names and inflected Polish city names.

**Reproduce** (prints the four runs above with their misses and false positives, and fails if any metric drops under its floor):

```bash
dotnet test tests/ExitInterviewAgent.Privacy.Tests --filter "Category=PiiEvaluation" --logger "console;verbosity=detailed"
```

A third corpus, `overmask.txt` (17 lines, 7 gold names), was written for the over-masking fixes of ADR-0063 and is **not** independent evidence; it is scored in fail-closed mode:
before the fix 30 findings, 23 of them false positives (precision 23.3 %, CI 12-41 %), after 7 findings and none (100 %, CI 65-100 %), recall 7/7 both times.
The dev and held-out rows above are identical before and after the fix.

The floors (`EvaluationTests.Floors`) sit a few points under these numbers. Raise them when the detector improves; do not lower
them to make a change pass. The numbers above were taken on 2026-10-05 from this command at the commit that introduced the
detector.

## Using it

```csharp
var detector = new PiiDetector(new PiiOptions
{
    AllowList = ["Zephyrix", "Orbit Suite"],   // the employer and product names of this interview
    FailClosed = true,
});

MaskResult result = detector.Mask(transcriptText);
// result.MaskedText: "... my manager [PERSON] ..."
// result.Findings: kind + offsets only; safe to log or put in a trace attribute count
```

Run the quotes through `QuoteVerifier.VerifyQuotes` against the **masked** transcript (see
[`../architecture/record-schema.md`](../architecture/record-schema.md)) and set `piiMasked` only when this step ran.

## Extending it

Add cues, lexicon entries and corpus lines together (`Corpus/*.txt`: `{{kind:text}}` marks gold spans); the evaluation command is the
acceptance test. A new language needs its own corpus and its own measured numbers in the table above, not an extrapolation from these.
