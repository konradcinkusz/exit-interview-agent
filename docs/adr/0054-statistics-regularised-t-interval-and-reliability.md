# 0054. Statistics: a regularised t interval, exact n, a reliability that moves with n

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: `metric-ethics` §2 (counter-metric in the same payload), §3 (no number leaves without its confidence), brief §6 ("always with uncertainty"),
  `research-documentation` (every number reproducible by a command).

## Context

A displayed cell has n between k (5) and a few dozen ratings on a 1-5 scale. The textbook Student t interval assumes roughly normal data and, at the extreme, gives a
**zero-width interval for a sample of identical ratings**, which is the most dangerous output a reader could take as certainty. Measured on the synthetic battery below,
the plain t interval covers the true mean in 22.9% of samples at n = 5 for a near-unanimous population (`Without_the_prior_a_near_unanimous_sample_is_badly_overconfident`).

Options considered: the percentile **bootstrap** (random, and degenerate for identical ratings: every resample is identical); a **Hoeffding** bound for bounded variables
(valid for every distribution, but the interval at n = 5 is wider than the scale); the **Wilson** interval (for proportions, not a mean of a 5-level scale); a library (a licence and
maintenance review for 150 lines of well-known mathematics, and nothing in the repository is allowed to be an unreviewed black box here).

## Decision

1. **n is exact** and printed with every mean. Banding n would hide nothing: the mean and interval are functions of n, and a banded n would only make the statistics
   unverifiable. Differencing is handled by the partition rule, not by blurring n ([ADR-0053](0053-disclosure-control-clean-partitions-and-k-per-cell.md)).
2. **The mean** is the sample mean, rounded to two decimals. **The interval** is a **variance-regularised Student t interval**, implemented from scratch (`StudentT`, `MeanInterval`):

   ```
   s2   = unbiased sample variance of the ratings
   s2r  = (v0 * s0sq + (n - 1) * s2) / (v0 + n - 1)       v0 = 6 pseudo-observations, s0sq = 2.5
   half = t(0.975; v0 + n - 1) * sqrt(s2r / n)
   interval = mean +- half, clipped to [1, 5], rounded outwards to two decimals
   ```

   The prior stops s2 = 0 from producing a point: five identical 5s give 3.8 to 5.0. It washes out with n. The constants were tuned on the synthetic battery by hand (a small
   grid, in a scratch program, then pinned by `IntervalCoverageTests`); **they are a judgement, not a result about real ratings** (none exist). The method is deterministic (no
   random numbers), so a snapshot is reproducible from its input.
3. **Measured coverage** (nominal 95%, 4000 replicates per cell, fixed seeds, ten distributions: uniform, skewed both ways, polarised, 50/50 at the ends, concentrated, peaked,
   near-unanimous, unanimous, and a rare low tail): the minimum over the battery is **92.4%** (n = 20, rare low tail), **93.3%** at n = 5 (polarised), and most cells are 96-100%. The test fails below
   90%. The table is printed by `dotnet test tests/ExitInterviewAgent.Signals.Tests --filter IntervalCoverageTests --logger "console;verbosity=detailed"`.
4. **Caveats stated with the number.** The interval undercovers a little for a bounded variable with a heavy rare tail, and it overcovers (is conservative) for concentrated
   distributions. It assumes the ratings in a cell are independent draws, which they are not (one tool's users, one employer's culture): it describes the sampling noise of the
   people who answered, not the employer's true level (OP-4, OP-1). Ratings are produced by a model reading an interview (OP-5): the interval does not include that error.
5. **Reliability** is a descriptor that moves with n relative to k: `low` below 2k, `moderate` below 6k, `high` from 6k (10 and 30 at k = 5). It describes the sample size, not the employer.
6. **Coverage** (the counter-metric) sits in the same object as the mean: the share of the cell's population (the employer, or the band) that rated the topic, banded. A
   topic most people skipped reads `low` however good its mean looks.
7. **No composite, no ranking** ([ADR-0053](0053-disclosure-control-clean-partitions-and-k-per-cell.md) item 8). How to read the pair: a wide interval means *early, not wrong*, and two intervals that overlap are not different.

## Consequences

- Intervals are wide at n = 5-10 on purpose; the UI says so (copy contract in [AGGREGATION §8](../privacy/AGGREGATION.md#8-api-contract-and-ui-copy-contract)).
- Changing a constant, the prior, or the level changes `RulesVersion` and republishes (ADR-0055).
- Trigger for revisiting: real ratings (or a defensible simulation of them) showing undercoverage that matters; the alternative on the shelf is a bootstrap-t or an exact ordinal method.
