# Aggregation: what is published, and what can and cannot be recovered from it

Status vocabulary as in [ADR-0017](../adr/0017-documentation-layout-and-claim-status.md). Everything in sections 1-6, 8 and 9 is **Implemented** (T10; the web view of section 8 by T10b) and
tested; section 7 says what the rules do **not** do. Decisions: [ADR-0052](../adr/0052-signals-module-boundary-input-port-and-store.md) (module, port, store),
[ADR-0053](../adr/0053-disclosure-control-clean-partitions-and-k-per-cell.md) (the rules), [ADR-0054](../adr/0054-statistics-regularised-t-interval-and-reliability.md) (statistics),
[ADR-0055](../adr/0055-publication-batches-snapshot-and-deletion-semantics.md) (publication), [ADR-0056](../adr/0056-signals-api-caching-rate-limits-and-demo-data.md) (API).
Companions: [signals module](../architecture/signals.md), [privacy design §5.5](DESIGN.md#55-aggregates-k-threshold-uncertainty-no-ranking-implemented-t10), [threat model T-01](../security/THREAT-MODEL.md),
[open problems](../OPEN-PROBLEMS.md), [metric ethics in the methodology](../eval/METHODOLOGY.md#14-aggregate-counter-metrics-t10).

Records are personal data and are never called anonymous ([ADR-0018](../adr/0018-records-are-treated-as-personal-data.md)). **Aggregates may be called "aggregated"**, and nothing stronger:
the rules below make small groups hard to isolate; they do not make anyone anonymous.

## 1. What is published, and what is not

Published, per employer that has at least one displayable cell: for each of six topics, a **rating count (n)**, the **mean with a 95% interval**, a **reliability** label, a **coverage** band, and (only when the rules
allow) a three-bin **rating distribution**, a **verification breakdown**, and **single-band cuts** by tenure, seniority and function. Each snapshot also says when its batch started, which rule set it was
built under, and k.

**Never** published, by any endpoint, store column or log line of the module: a record, a quote, an interview id, a receipt code, a confidence value, a record's bands, an exact respondent count, the
number of people who did *not* rate a topic, a count or an interval from fewer than k ratings, how many cells were withheld, a score for an employer, an average across topics, a rank, a "best" or "worst", a percentile against other
employers, or an employer list in any order but alphabetical. The anti-goals are enforced by absence ([metric-ethics §1](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/METRIC-ETHICS.md)):
there is no field, column, query or route that could express them (tests in section 10).

## 2. The rules

| # | Rule | Where in code | Test |
|---|---|---|---|
| R1 | **k applies to every displayed cell**: a topic's rating count for an employer must be at least k (default 5, floor 3, `Signals:MinimumGroupSize`). Below k the topic is `insufficient_data`, with no number. | `StandardDisclosurePolicy.IsDisplayable`, `EmployerAccumulator.Complete` | `BuilderTests` (k and k-1, per topic, configurable k), `PropertyTests` |
| R2 | **Single-band cuts only**: tenure, seniority, function, one at a time. No cross-product. | `Vocabulary.Dimensions`, `EmployerAccumulator.BuildCut` | `There_is_no_cross_product_of_bands` |
| R3 | **A cut is a clean partition or it is withheld whole**: every band empty or at least k, and the records that left the optional band out zero or at least k. | `StandardDisclosurePolicy.Plan` | `AdversaryTests` (exhaustive), `BuilderTests`, `PropertyTests` |
| R4 | **The distribution (three fixed bins) and the verification levels are published whole or not at all**, by the same rule, on the employer x topic cell only. | `EmployerAccumulator.Groups` | `Distribution_and_verification_are_published_only_as_clean_partitions` |
| R5 | **The respondent count is a band** with a lower edge of at least k; **coverage** is a band (`high`, `medium`, `low`). | `DisclosureRules.RespondentsBand`, `Coverages.For` | `BuilderTests` |
| R6 | **Batched publication**: one snapshot per period (default a day), never on a submission, a deletion or a request. | `SnapshotPublisher`, `SnapshotPublisherService` | `PublisherTests`, `SignalsEndpointTests` |
| R7 | **Coarse time**: the snapshot carries the start of its period only. | `SnapshotPublisher.PeriodStart` | `The_generation_time_is_the_start_of_the_batch...` |
| R8 | **Deletions take effect at the next batch**, and the API says so. | rebuild-from-source design; `deletionsAppearAtNextPublication` | `A_deletion_after_publication_takes_effect_at_the_next_batch_not_before` |
| R9 | **Uncertainty on every number**, in the same object: n, mean, interval, reliability, coverage. | `StatsView`, `MeanInterval` | `StatisticsTests`, `IntervalCoverageTests` |
| R10 | **One answer for "not shown"**: an unknown employer and one below k get a byte-identical 404. | `SnapshotReader.GetAsync`, `SignalsEndpoints` | `An_employer_below_k_and_an_unknown_employer_get_byte_identical_answers` |
| R11 | **No composite, no ranking, no per-employer score, no best/worst, no percentile**; alphabetical lists. | the published shape, the store, the reader | `There_is_no_score_no_composite_...`, `The_store_holds_published_aggregates_only...`, `The_reader_exposes_no_query_by_value`, `The_list_holds_employers_...alphabetically` |
| R12 | **Nothing below k is returned, logged, cached or emitted**: not in a body, an error, a log line, a metric label or a cache header. | whole module | `No_signals_response_contains_any_field_or_value_of_a_record`, `Nothing_per_record_or_per_employer_is_logged`, `Metrics_carry_the_outcome_label_only` |

## 3. Clean partitions, by example

An employer with 26 ratings of "culture". Tenure bands: `1y_3y` 12, `3y_5y` 11, `gt_10y` 3.

- Per-cell rule only: show 12 and 11, hide 3. A reader computes 26 - 12 - 11 = **3**, and the hidden band's mean from the sums. The three people are exposed.
- Textbook complementary suppression: hide the smallest shown cell too, so the remainder is 3 + 11 = 14. Safe for this snapshot (but see section 4).
- **This system: the tenure cut for this topic is withheld whole** (`suppressed`). The other cuts (seniority, function), the topic's n, mean and interval are unaffected.

With a clean partition, say `1y_3y` 10 and `3y_5y` 10 (the other four bands empty), the cut is published: two cells with statistics and four `none`. A `none` is a zero, which is not a group of people.

Records that left an optional band out (a person may leave out seniority or function, [ADR-0007](../adr/0007-record-context-bands.md)) are a group of their own. 18 say `mid`, 2 say nothing: the
two are exactly 20 - 18, so the seniority cut is withheld.

## 4. Why not the textbook rule: two snapshots one record apart

The adversary adds a record of their own, or removes it with a receipt code, between two batches, reads every allowed cut in both, and subtracts. Against the textbook rule that isolates one person
(worked example in [ADR-0053](../adr/0053-disclosure-control-clean-partitions-and-k-per-cell.md): seniority bands 5, 4 and one record outside; the adversary adds a "senior" record). The clean-partition rule gives:

> **Within one snapshot**, every group of people that adding and subtracting displayed numbers can isolate has 0 or at least k members.
> **Across two snapshots that differ by the adversary's own record**, every group of *other* people they can isolate has 0 or at least **k - 1** members.

Evidence: `AdversaryTests` enumerates **every** partition of three bands plus an "outside" group with counts 0 to 2k, for k = 3, 4 and 5, and every one-record neighbour (added to a band, added outside,
removed from a band, removed from outside), computing what an adversary holding both snapshots can determine; `PropertyTests` does the same through the full pipeline over 300 and 250 random populations per k (fixed seed,
skewed bands, unanimous, polarised, optional bands left out). Four mutants (no complementary suppression; the textbook rule; a threshold off by one; a forgotten "outside" group) are each caught; the textbook one
passes the single-snapshot check and fails the two-snapshot one, which is the reason for the rule.

## 5. Statistics

- **n** is exact. **Mean** is the sample mean to two decimals.
- **Interval**: a Student t interval with a variance regularised towards a prior of 6 pseudo-observations of variance 2.5, clipped to the 1-5 scale, rounded outwards (formula in [ADR-0054](../adr/0054-statistics-regularised-t-interval-and-reliability.md)).
  Five identical ratings of 5 give 3.8 to 5.0, not a point. Implemented from scratch (no dependency to vet); the t quantile is checked against the textbook table to three decimals.
- **Measured coverage of the nominal 95%** (synthetic distributions, 4000 replicates, fixed seeds; the minimum per row, then the weakest distribution):

  | n | 5 | 8 | 12 | 20 | 40 |
  |---|---|---|---|---|---|
  | minimum over ten distributions | 93.3% (polarised) | 93.6% (50/50 at the ends) | 93.3% (polarised) | 92.4% (rare low tail) | 94.2% (rare low tail) |

  The plain t interval on a near-unanimous population at n = 5 covers 22.9%; the regularised one 100%. Reproduce: `dotnet test tests/ExitInterviewAgent.Signals.Tests --filter IntervalCoverageTests --logger "console;verbosity=detailed"`.
  **Caveats:** the constants were tuned on this battery (a judgement, not a result); the cells' ratings are not independent draws; the ratings are a model's reading of an interview, whose error the interval does not
  contain; nothing here has been checked against real ratings (none exist).
- **Reliability** moves with n relative to k: `low` below 2k, `moderate` below 6k, `high` from 6k (10 and 30 at k = 5). **Coverage** is the counter-metric: the share of the cell's population that rated the topic, banded
  (`high` 75% or more, `medium` 40-74%, `low` below 40%).
- **How to read a cell:** the mean and the interval are one fact. A wide interval means *early, not wrong*; two overlapping intervals are not different; `low` coverage means most respondents did not rate this topic, however
  good the mean looks.

## 6. Batching and deletion, said plainly

The snapshot is rebuilt from what is stored **once per period**. Between batches the numbers do not move: a new record, a deletion by receipt code and a retention purge all appear only at the next batch. A deleted record
is **still counted until then**. After the next batch the cell is recomputed from the remaining records and disappears if it falls below k. The batch start is shown (never finer than an hour); the time the run finished is not stored.

## 7. What k does not protect against

K is a convention, not a guarantee. Stated candidly, each with the test or open problem behind it:

1. **A malicious account with its own record.** Adding one record to an employer with k - 1 others makes the cell appear, and "everything minus my record" is exactly those k - 1 people. The design bounds this at **k - 1**
   (default: a group of 4), no lower; an adversary with *m* accounts (the ledger limits one per employer per account, not the number of accounts; [OP-1](../OPEN-PROBLEMS.md#op-1-real-employment-verification), T-10) gets **k - m**. Test:
   `The_known_boundary_k_minus_one_other_records_are_recoverable...` documents the boundary instead of hiding it. Raise k for small employers; the real fix is verification.
2. **Side knowledge.** An adversary who knows who else submitted, or that a group of five contains four known people, can eliminate ([T-01](../security/THREAT-MODEL.md), [OP-3](../OPEN-PROBLEMS.md#op-3-tenure-and-role-band-granularity-vs-small-groups)).
3. **Homogeneity.** A unanimous cell is shown (with a wide interval): everyone in it gave that rating, so a person known to be in the cell has a known rating. Suppressing unanimous cells would be an l-diversity rule, would
   bias what is shown towards polarised employers, and the pattern of suppression would itself tell ([OP-20](../OPEN-PROBLEMS.md#op-20-homogeneous-cells-are-shown)).
4. **The pattern of what is withheld.** A withheld cut says that some band in it has between 1 and k - 1 ratings, or that a group left out of the band has. It does not say which ([OP-21](../OPEN-PROBLEMS.md#op-21-what-is-withheld-is-itself-a-signal)).
5. **Differences across more than one record.** The cross-snapshot guarantee is for one record of the adversary's own moving between two batches. A batch in which a handful of known people submitted, with nothing else changing,
   exposes their joint contribution (it is bounded by the batch size and the period: a longer period dilutes it; [OP-19](../OPEN-PROBLEMS.md#op-19-k-is-a-convention-and-small-batches-expose-small-differences)).
6. **Utility.** At employers with a small band, whole cuts disappear (R3). That is the price of 1, and it is visible in the demo ([OP-22](../OPEN-PROBLEMS.md#op-22-clean-partitions-withhold-more-than-a-textbook-rule-would)).
7. **Not covered at all:** fabricated records (T-10), model error in ratings (OP-5), who the respondents are (OP-4), and an operator with the database, the key and live traffic (T-13).

## 8. API contract and UI copy contract

`GET /api/v1/signals/employers` and `GET /api/v1/signals/employers/{employerRef}`; policy `account`; opening them to anonymous readers is a later, separate decision. Shapes: `ExitInterviewAgent.Contracts` (`SignalsContracts.cs`).
Statuses: topic `ok` | `insufficient_data`; cut `published` | `suppressed`; band `ok` | `none` | `suppressed`. Codes: `SIGNALS_EMPLOYER_NOT_FOUND` (404, also for an employer below k), `SIGNALS_INVALID_EMPLOYER_REF` (400),
`rate_limited` (429, `Retry-After`). Caching: `private, max-age` to the next batch, `ETag`, `Last-Modified` = batch start, `304` on `If-None-Match`.

A view built on this API must show, in words the reader sees (the strings are a contract, not a suggestion; a UI may translate them but must not drop their meaning).
**Applied by the web panel (T10b, Implemented and tested):** the right-hand column is shown verbatim by `m.signals` in `web/app/lib/messages/en.ts`, asserted by Vitest (templates and rendered markup) and by the browser suite;
the "Where" column maps to: every aggregate = the note at the top of the list and of each employer page; next to the snapshot date = `SnapshotNote`; every mean = `StatLine` (one string) with the "wide interval" sentence under each figure;
coverage = the contract wording in lower case under each figure and in each band row; `insufficient_data` = no digit anywhere in the section (tested); a `suppressed` cut = the sentence and no band, no table, no cell (the reader drops cells before the view sees them);
the list = the sentence above the list; deletion = `m.deleteSubmission.publishedFigures` on `/delete-submission`, shown before and after a deletion. No deviation from the wording; the only addition is a per-band sentence for a band that arrives `suppressed`
("Hidden to protect small groups."), which the standard policy never produces inside a published cut. Decisions: [ADR-0067](../adr/0067-signals-pages-render-the-api-and-derive-nothing.md) to [0069](../adr/0069-one-answer-for-nothing-to-show-in-the-ui.md).

| Where | Must say |
|---|---|
| every aggregate | "Aggregated from records submitted by users of this tool. Employment is claimed, not verified." (until a real verifier exists, [OP-1](../OPEN-PROBLEMS.md#op-1-real-employment-verification)) |
| next to the snapshot date | "Updated {date}. Figures change once per update, not when someone submits. A record deleted after this date is still counted until the next update." |
| every mean | the interval and n, in the same line: "{mean} (95% interval {lower}-{upper}), {n} ratings"; and "A wide interval means early, not wrong." |
| coverage | "{high/medium/low}: the share of respondents who rated this topic." |
| `insufficient_data` | "Not enough responses to show this topic." Never a number of responses, never "{n} of {k}". |
| a `suppressed` cut | "This breakdown is hidden to protect small groups." Never which band. |
| the list | "Employers are listed alphabetically. This tool does not rank employers." |
| deletion (T9 receipt flow) | "Deleting your record removes it from the stored data now. Published figures drop it at the next update." |

## 9. Configuration

| Key | Default | Meaning |
|---|---|---|
| `Signals:Enabled` | `true` | the publisher runs; `false` serves the last snapshot and shows `signals` as not configured in `/health` |
| `Signals:MinimumGroupSize` | `5` | k; at least 3, a lower value stops the service at startup |
| `Signals:PublishInterval` | `1.00:00:00` | the batch period: whole hours, 1 hour to 7 days |
| `Signals:CheckInterval` | `00:05:00` | how often the publisher looks at the clock (not the cadence) |
| `Signals:RequestsPerMinute` | `30` | per-account budget on both endpoints |
| `Signals:Demo:Mode` / `Signals:Demo:Seed` | `Off` / `42` | demo data: `Seed` or `Remove`, **Development only** (any other environment stops at startup) |

## 10. How to reproduce every claim here

```bash
dotnet test tests/ExitInterviewAgent.Signals.Tests                       # rules, statistics, attack suite, mutants, publisher
dotnet test tests/ExitInterviewAgent.InterviewService.Tests --filter Signals   # API, adapter, demo data, PostgreSQL (needs TEST_POSTGRES_CONNECTION)
dotnet test tests/ExitInterviewAgent.Signals.Tests --filter IntervalCoverageTests --logger "console;verbosity=detailed"   # the coverage table
ASPNETCORE_ENVIRONMENT=Development Signals__Demo__Mode=Seed dotnet run --project src/ExitInterviewAgent.InterviewService   # the demo, in memory (Development only)
```

The PostgreSQL tests are skipped, and reported as not run, without `TEST_POSTGRES_CONNECTION`.
