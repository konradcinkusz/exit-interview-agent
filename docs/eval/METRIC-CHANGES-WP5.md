# Gated metrics that move with protocol 1.3 (WP5)

For ADR-0040: each gated behaviour metric that changes against the committed baseline, with the reason. Mock profile only; the numbers are
from `scripts/run-evals.sh report` on the WP5 branch, not from a real model. Baseline values are the committed `evals/baseline.json`.

| Metric | Committed baseline | WP5 (mock) | Reason |
|---|---|---|---|
| `opr` (over-probe, Q-13) | 0/55 (0%) | 3/78 (3.8%) | Changed by design (spec §6.1 Q-13). The old definition counted any probe after a labelled reply, Ladder and Consequence included. The gate now counts only Example probes after a reply labelled specific or decline (`TurnMetrics.OprLabelled`). The 3 that remain are Example requests after a decline. |
| `fuv` (follow-up on vague) | 33/33 (100%) | 19/38 (50%) | Changed by design (spec §3.4 rule 7). A light topic (rated 1 to 5) gets no example request for a vague answer that has no number, so every vague reply on a light topic is in the denominator and unprobed. Not a regression on the core topics; the owner decides whether `fuv` should be restricted to core and reason topics (a metric redefinition, not made here). |
| `depth` (concrete quote per covered topic) | 110/156 (70%) | 102/186 (55%) | Changed by design. Light topics are answered with a rating, and a rating carries no concrete detail. The corpus also gained two scenarios (hap-005, hap-006) whose plain replies add covered topics without concrete detail. The numerator is unchanged (102). |
| `clarified` (contradiction clarified once, B-04) | 3/3 (100%) | 0/3 (0%) | Not fixed in this change. A contradiction is two answers on one topic. Under protocol 1.3 the contradictory persona's contradicted topic is a light topic (the planner gives its core slots to topics raised earlier), and a light topic gets one reply, so no contradiction is observable. Spec §3.4 has no clarification rule for light topics. This needs an owner decision: change the persona so the contradicted topic is core, or extend the spec. The request-for-clarification ("nie rozumiem pytania") signal the review asked about is not in the spec and is not implemented. |
| `degraded_ok` (B-08) | 7/7 | 7/7 | Not a behaviour change. The grader accepted only the 1.2 wording; protocol 1.3 wording (warm-up, rating, retention, anything else) was counted as fabricated. The check now accepts all fixed protocol wording in every register and still rejects any other question (`Layer1Grader.ProtocolQuestions`, test `Every_fault_run_that_asked_a_question_fell_back_to_protocol_wording_and_counts_as_degraded_ok`). |

Not gated and not changed in meaning: `coverage`, `released`, `redirected`, `edge_case`, `probe_on_core`, `no_probe_on_light_mid`,
`questions_within_cap`. `yield_concrete` (WP6) is a per-interview count, reported without an interval; it should become a mean in WP6.
