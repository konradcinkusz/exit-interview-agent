# 0063. PII detector: the obfuscated-email rule is anchored on its marker; over-masking after a closing tag and on topic nouns is fixed

- Status: accepted
- Date: 2026-10-05
- Principle or guide served (or deviated from): [ADR-0010](0010-pii-detector-deterministic-heuristics.md) (bounded quantifiers, honest limits), `SECURITY-REVIEW` (input-driven cost, errors fail closed), `TESTING-STRATEGY` (a test that cannot fail is worse than none; measure before and after)

## Context

Two defects in `ExitInterviewAgent.Privacy`:

1. `PatternRules.EmailObfuscated` was one regular expression that began at every letter or digit and scanned up to 64 characters ahead for an `[at]` marker. On a long unbroken token that is the cost of the rule: about 7 microseconds per character, against the 2 s per-rule budget ([`RegexBudget`](../../src/ExitInterviewAgent.Privacy/RegexBudget.cs)). A single token of about 300 000 characters would have exhausted the budget and thrown, which every caller reads as "do not submit".
2. In fail-closed mode the detector masked text it should not: the first word after a closing transcript tag (`</TRANSCRIPT_DATA>`, `<<<END_TRANSCRIPT_DATA nonce>>>`) because `>` was not read as the end of a sentence, and interview-topic nouns ("Compensation and Pay", "Benefits and Growth", "Wynagrodzeniu i Premiach") because an unknown capitalised word in mid-sentence is masked when uncertain.

## Decision

**Measured before** (Release build, 200 000-character inputs, `dotnet test tests/ExitInterviewAgent.Privacy.Tests --filter "Category=RuleCost" --logger "console;verbosity=detailed"`; one run on a shared cloud container, so read the ratios, not the absolute values):

| Input (200 000 chars) | `EmailObfuscated` before | `AddObfuscatedEmails` after |
|---|---|---|
| unbroken letters | 1358 ms (6791 ns/char) | 0.0 ms |
| unbroken digits | 1223 ms | 0.0 ms |
| 60-letter words | 806 ms | 0.0 ms |
| `1-` repeated | 671 ms | 0.0 ms |
| `a.` repeated | 625 ms | 0.1 ms |
| realistic transcript with 2 emails per exchange | 54.5 ms | 4.3 ms |
| `a[at]` repeated (marker-dense) | 37.7 ms | 70.3 ms |
| `a [at] ` repeated | 45.8 ms | 31.4 ms |

- **The rule was made cheap without losing detection.** The marker (`[at]`, `(at)`, `{at}`) is found with a linear regex; the local part (at most 64 characters, optional one space before the marker) is read backwards by hand; the tail (`host`, then 1 to 6 `.`/`[dot]`/`(dot)` parts) is a `\G`-anchored regex. A match never starts inside the previous one, as with `Regex.Matches`.
- **Detection is proved, not asserted.** The previous single regex is kept in `ObfuscatedEmailRegressionTests` as the oracle. Every line of `Corpus/email-obfuscated.txt` (34 samples, all detected by the old rule; Unicode, case variants, 64-character local parts, spaces and punctuation around the markers) must give identical spans, and 20 000 seeded random strings built from marker, separator, Unicode and length-boundary fragments must give identical spans too. Both passed on the first run that mattered (the only failure was a corpus line, `{dot}`, which the old rule never detected; it was removed, not made to pass).
- **The one input that got slower** is the marker-dense one (`a[at]` repeated, 70 ms per 200 000 characters against 38 ms). That is still linear and about 350 ns per character; the trade is deliberate.
- **All other rules were measured on the same inputs** (every static `Regex` field of `PatternRules` and `NameRules`, found by reflection): none timed out, none is superlinear; the slowest after this change is `NameRules.RelationReversed` at about 1.1 to 1.8 microseconds per character on capitalised and hyphenated words. A reflection test asserts that every rule has a finite match timeout within the budget, that none exceeds 8 s on 200 000 characters of any adversarial input (a quadratic rule needs minutes), that quadrupling the input does not multiply the obfuscated-email scan by more than 12 (linear is about 4, quadratic about 16), and that a 5 MB unbroken token passes it. The per-rule 2 s budget is unchanged.
- **Closing tags end a text.** A `>` that closes a tag (a `<` within the previous 80 characters on the same line) now counts as a sentence boundary, so the word after `</TRANSCRIPT_DATA>` is read as a sentence start, as after a full stop.
- **Interview-topic nouns are on the stop list** (pay, compensation, salary, benefits, growth, workload, culture, and the Polish forms), together with the imperatives `summarise`/`extract`. They are never a person; before, only fail-closed masked them, as the unknown capitalised word it could not classify.
- **Measured** with the repository's evaluation command (`dotnet test tests/ExitInterviewAgent.Privacy.Tests --filter "Category=PiiEvaluation" --logger "console;verbosity=detailed"`):
  - `dev` and `held-out`, both modes: identical before and after (dev: recall 68/68, precision 100.0 % default and 91.9 % fail-closed; held-out: recall 47/49 default and 49/49 fail-closed, precision 100.0 % and 94.2 %). Recall did not drop in any run.
  - New `overmask.txt` (17 lines, 7 gold names, written for these two defects, so it shows the fix and is not independent evidence), fail-closed: before 30 findings, 7 true positives, 23 false positives, precision 23.3 % (CI 12-41 %); after 7 findings, 0 false positives, precision 100 % (CI 65-100 %); recall 7/7 before and after (the unknown given names after "and", a surname after a title, and a name right after a closing tag are still masked).

## Consequences

- The obfuscated-email scan is linear and about 350 ns per character at worst; a 5 MB token takes tens of milliseconds, so the 2 s budget is no longer a limit anyone can reach with an unbroken token. It is still the backstop for everything else.
- Topic nouns are a finite list. A topic that is not on it ("Autonomy and Mentoring") is still masked in fail-closed mode, which is the intended price of that mode; [OP-30](../OPEN-PROBLEMS.md#op-30-fail-closed-over-masking-of-capitalised-topic-words-is-bounded-only-by-a-list) records it. The `{dot}` form of an obfuscated email is not detected, and was not before ([OP-31](../OPEN-PROBLEMS.md#op-31-obfuscated-email-spellings-beyond-the-bracketed-forms-are-not-detected)).
- The size ceiling for the kernel is not affected (the change is in the Privacy library, not `ServiceDefaults`). No model, no network, no dependency was added.
- The evaluation floors gained an over-masking row (recall 1.0, precision 0.95); the dev and held-out floors are unchanged.
