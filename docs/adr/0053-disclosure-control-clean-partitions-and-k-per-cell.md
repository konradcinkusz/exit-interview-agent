# 0053. Disclosure control: k per displayed cell, clean partitions, no cross-products

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: brief §6 and [ADR-0019](0019-brief-amendments-from-the-t3-legal-privacy-review.md) (c), [ADR-0007](0007-record-context-bands.md) (K applies to every
  published cut), threat model T-01, `metric-ethics` §1, §3, §5, `testing-strategy` (a test that cannot fail is worse than none).

## Context

K = 5 applied only to an employer's total protects nothing once bands are shown: the total and all but one band expose the last band by subtraction, and a small
band is a handful of people (T-01). ADR-0019 decided "K per displayed cell, no cross-products, batched". This ADR decides what that means precisely, because the textbook rule
is not safe.

**The textbook rule** ("suppress every cell below k; then, while the records outside the shown cells are fewer than k, suppress the smallest shown cell too") is
sound against one snapshot. It is not sound against two snapshots one record apart, which is exactly the adversary the task names:

> Seniority bands over an employer's rated records: `mid` 5, `senior` 4, and one record that left seniority out. The textbook rule shows `mid` (5) and withholds
> the other five records together. The adversary adds one `senior` record of their own. Now `mid` 5 and `senior` 5 would both show with one record outside; the rule
> withholds the smaller (a tie: `mid`) and shows `senior`. The adversary has `mid` = 5 from the first snapshot and `senior` = 5 - 1 = 4 others from the second, and the first
> snapshot's "five withheld" minus 4 is **one person**: the one who left seniority out, with their rating.

The exhaustive search over small partitions finds this scenario (`AdversaryTests`), and the pipeline test reproduces it end to end
(`The_textbook_rule_is_flagged_...`).

## Decision

1. **k is applied to the number of ratings in a displayed cell.** The cell is employer x topic (a topic's rating count, not the employer's record count). Default 5
   (`Signals:MinimumGroupSize`), at least 3 (a floor that fails startup: the guarantee below is "k - 1 other people", and 2 would promise one). Below k the topic is
   `insufficient_data`: no number, and nothing that says how many records exist.
2. **Cuts are single-band only**: employer x topic x tenure, x seniority, x function, one at a time. There is no cross-product (no "senior engineers with 3-5 years"):
   a cell is never defined by two bands.
3. **A cut is a clean partition or it is not published.** Let the cut's cells be the bands of one dimension, and let *unassigned* be the records outside every band
   (an optional band that was left out). The cut is published only if **every band is empty or holds at least k, and unassigned is zero or at least k.** Otherwise
   the whole cut is withheld (`suppressed`), including its large bands. Empty bands in a published cut are reported as `none` (a zero is not a group of people).
   This is complementary suppression taken to its limit: not "hide one more cell until the remainder is big enough" but "hide the cut".
4. **The same rule governs the fixed partitions inside a displayed cell:** the rating distribution (three fixed bins: 1-2, 3, 4-5) and the verification levels
   (unchecked, unverified, verified) are published whole, each group 0 or at least k, or not at all. They are shown on the employer x topic cell only, never on a
   band cell: a breakdown inside a band would be a cross-product by another name, and a remainder of a cut could then carry a group below k in the breakdown.
5. **The employer's respondent count is shown only as a band** whose lower edge is at least k (`5-9`, `10-24`, `25-49`, `50+` at k = 5; the edges scale with k). Coverage (the
   counter-metric) is shown as a band too (`high` 75% or more, `medium` 40-74%, `low` below 40%): the number of people who did *not* rate a topic is never printed, because
   next to an exact n it would be a count below k.
6. **What the rules guarantee, and for whom.** Within one snapshot, every group of people the reader can isolate by adding and subtracting what is shown has 0 or at
   least k members. For an adversary whose own record is added to or removed from the data between two snapshots, every group of *other* people they can isolate has
   0 or at least **k - 1** members (their own record is the one that moved). Both are checked exhaustively for k = 3, 4, 5 over every partition of three bands plus unassigned with counts up to 2k and
   every one-record neighbour, and by seeded property tests over random populations through the whole pipeline.
7. **The textbook rule, a per-cell-only rule, an off-by-one threshold and a rule that forgets the unassigned remainder are kept in the test project as mutants.** The attack
   suite must find a leak in each; it does (see `Mutants.cs`). The textbook mutant is the one that passes the single-snapshot check and fails the two-snapshot one.
8. **No composite, no cross-topic average, no ranking, no best/worst, no percentile.** The published shape has no field for them (a reflection test lists the allowed
   property names), the store has no column to sort by, the reader has three methods (`CurrentAsync`, `ListAsync`, `GetAsync`) and no query by value, and the list endpoint is alphabetical.

## Consequences

- **Utility is lost on purpose.** At an employer with one small band (a tenure band of three people), the whole tenure cut disappears for that topic, even though the other bands
  are large. Tenure is the dimension most likely to be withheld at a mid-sized employer. Trigger for revisiting: a measured share of withheld cuts that makes the cuts useless; the
  answer is recoding (merging adjacent bands), which changes the wire vocabulary and needs ADR-0009's process.
- **What k does not do** (stated again in [AGGREGATION §7](../privacy/AGGREGATION.md#7-what-k-does-not-protect-against), [OP-3](../OPEN-PROBLEMS.md#op-3-tenure-and-role-band-granularity-vs-small-groups) and OP-18 to OP-21): it does not stop an adversary with
  several accounts (each extra record lowers the bound by one: k - m), nor one who knows who submitted, nor homogeneity (a unanimous cell shows that everyone in it gave that rating),
  nor the pattern of what is withheld (a withheld cut says some band is small). It is a convention, not a guarantee.
- Distribution and verification are rarely shown below about 15 ratings (three bins of at least 5); that is the price of the same rule for every partition.
