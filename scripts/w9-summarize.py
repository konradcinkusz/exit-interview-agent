#!/usr/bin/env python3
"""W9: turn w9-out/results.csv (written by scripts/w9-run.sh) and your filled scorecard.csv into the numbers and the verdict of the report.

    python3 scripts/w9-summarize.py --results w9-out/results.csv --scorecard w9-out/scorecard.csv [--model <id>]

Prints Markdown to paste into docs/eval/w9/report-template.md. The pass rules are the ones in docs/eval/w9/README.md; they are constants
at the top of this file, so a change of rule is a change of code that review can see. No network, no keys, no dependencies.
"""
import argparse, csv, statistics, sys

CRITERIA = ["reacts_to_reply", "polish_language", "no_repeats", "neutral", "record_quality", "tiles_quality", "ending", "natural_flow"]
MEAN_MIN = 1.5            # the mean of the eight criterion means (overall quality)
FLOOR_MIN = 1.0           # no single criterion may have a mean below this
REPLACED_MAX = 0.20       # share of interviewer questions replaced by the fixed protocol text (gate)
REPLACED_TARGET = 0.10    # protocol 1.3 target, reported, not gated
STOCK_OPENER_TARGET = 0.15  # protocol 1.3 target: share of interviewer turns that open with a stock phrase, reported, not gated
COST_MEDIAN_MAX = 0.40    # in the currency of --price-in/--price-out (USD in the plan)
COST_MAX = 0.90
MUST_PASS = {"5", "6"}    # the two scenarios that failed in pre.5/pre.6: both need >= 1.5 averaged over the criteria
NATURAL_FLOW_SCENARIOS = {"1", "4"}   # protocol 1.3: the talkative and the vague interview must feel like a conversation that followed the person
NATURAL_FLOW_MIN = 1.5
SKIP_QUESTION_SCENARIOS = {"9"}   # withdrawn consent: no record, no tiles, no questions to judge


def rows(path, model):
    with open(path, newline="", encoding="utf-8") as f:
        out = list(csv.DictReader(f))
    return [r for r in out if not model or r.get("model") == model]


def num(v):
    try:
        return float(v)
    except (TypeError, ValueError):
        return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--results", required=True)
    ap.add_argument("--scorecard", required=True)
    ap.add_argument("--model")
    a = ap.parse_args()
    res, sc = rows(a.results, a.model), rows(a.scorecard, a.model)
    if not res or not sc:
        sys.exit("No rows for that model in results.csv or scorecard.csv.")

    failures = []
    print("## Costs and wording\n")
    print("| Scenario | exit | calls | tokens in/out | latency s | cost | replaced / questions |")
    print("|---|---|---|---|---|---|---|")
    costs, replaced, asked = [], 0, 0
    for r in res:
        c, rep, q = num(r["cost"]), num(r["replaced_questions"]), num(r["interviewer_questions"])
        if c is not None: costs.append(c)
        if rep is not None and q is not None and r["scenario"] not in SKIP_QUESTION_SCENARIOS: replaced += rep; asked += q
        print(f"| {r['scenario']} | {r['exit_code']} | {r['calls']} | {r['tokens_in']}/{r['tokens_out']} | {r['latency_s']} | {r['cost'] or 'n/a'} | {r['replaced_questions']} / {r['interviewer_questions']} |")
        if r["extractor_codes"]:
            failures.append(f"scenario {r['scenario']}: extractor codes {r['extractor_codes']}")
    if costs:
        print(f"\nCost: median {statistics.median(costs):.4f}, max {max(costs):.4f}, total {sum(costs):.4f}.")
        if statistics.median(costs) > COST_MEDIAN_MAX: failures.append(f"median cost {statistics.median(costs):.4f} > {COST_MEDIAN_MAX}")
        if max(costs) > COST_MAX: failures.append(f"max cost {max(costs):.4f} > {COST_MAX}")
    else:
        print("\nCost: not recorded (run with --price-in and --price-out).")
        failures.append("no cost recorded")
    share = replaced / asked if asked else 0.0
    print(f"Wording: {int(replaced)} of {int(asked)} interviewer questions were replaced by fixed protocol text ({share:.0%}).")
    if share > REPLACED_MAX: failures.append(f"replaced-question share {share:.0%} > {REPLACED_MAX:.0%}")

    print("\n## Scorecard (0 bad, 1 medium, 2 good)\n")
    print("| Criterion | mean | min | n |")
    print("|---|---|---|---|")
    means = []
    for c in CRITERIA:
        vals = [num(r.get(c)) for r in sc if num(r.get(c)) is not None]
        if not vals:
            print(f"| {c} | n/a | n/a | 0 |"); failures.append(f"{c}: no scores"); continue
        m = statistics.mean(vals); means.append(m)
        print(f"| {c} | {m:.2f} | {min(vals):.0f} | {len(vals)} |")
        if m < FLOOR_MIN: failures.append(f"{c}: mean {m:.2f} < {FLOOR_MIN} (floor)")
    if means:
        overall = statistics.mean(means)
        print(f"\nOverall mean of the criteria: {overall:.2f}.")
        if overall < MEAN_MIN: failures.append(f"overall mean {overall:.2f} < {MEAN_MIN}")
    for r in sc:
        if r["scenario"] in MUST_PASS:
            vals = [num(r.get(c)) for c in CRITERIA if num(r.get(c)) is not None]
            m = statistics.mean(vals) if vals else None
            if m is None or m < MEAN_MIN: failures.append(f"scenario {r['scenario']} must pass: average {m if m is None else round(m, 2)} < {MEAN_MIN}")

    print("\n### Natural flow (protocol 1.3)\n")
    for r in sc:
        if r["scenario"] in NATURAL_FLOW_SCENARIOS:
            v = num(r.get("natural_flow"))
            print(f"- scenario {r['scenario']}: natural_flow {'n/a' if v is None else f'{v:.0f}'}")
            if v is None or v < NATURAL_FLOW_MIN: failures.append(f"scenario {r['scenario']} natural_flow {'not scored' if v is None else v} < {NATURAL_FLOW_MIN}")

    print("\n## Protocol 1.3 targets (reported, not gated)\n")
    print(f"- Questions replaced by fixed text: {share:.0%} against a target of {REPLACED_TARGET:.0%} ({'met' if share <= REPLACED_TARGET else 'not met'}).")
    openers, turns = sum(num(r.get("stock_openers")) or 0 for r in res), sum(num(r.get("interviewer_turns")) or 0 for r in res)
    if turns:
        print(f"- Stock-phrase openers: {openers / turns:.0%} of interviewer turns against a target of {STOCK_OPENER_TARGET:.0%} ({'met' if openers / turns <= STOCK_OPENER_TARGET else 'not met'}).")
    else:
        print("- Stock-phrase openers: not recorded (no transcripts).")
    words, answers = sum(num(r.get("interviewee_words")) or 0 for r in res), sum(num(r.get("interviewee_turns")) or 0 for r in res)
    print(f"- Mean interviewee words per answer: {words / answers:.1f} (no target; a higher number is the signal that the interview draws people out)." if answers else "- Mean interviewee words per answer: not recorded.")

    print("\n## Verdict\n")
    if failures:
        print("**NOT PASSED.** " + "; ".join(failures) + ".")
        sys.exit(1)
    print("**PASSED** on every rule in docs/eval/w9/README.md. This is a verdict on ten interviews played by one person; it is not a quality guarantee.")


if __name__ == "__main__":
    main()
