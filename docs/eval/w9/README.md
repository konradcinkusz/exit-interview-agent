# W9: ten real Polish interviews with a real model

Status: **protocol and tools ready, not run.** Nothing here has been run with a real model. The run needs the owner's API key and the owner's
judgement of the Polish; the numbers and the verdict come from [`scripts/w9-summarize.py`](../../../scripts/w9-summarize.py). W9 is the gate of the
web plan ([section 7](../../architecture/web-app-plan.md)): if the Polish interview or the tiles are not good with a real model, nothing after it is worth
launching. It also fixes the price (`Billing:PriceMinorUnits`) from measured cost, not from a guess.

## What W9 decides

1. Is the Polish interview good with a real model (reacts to what you wrote, correct Polish, no repeated questions, neutral)?
2. Are the draft texts (tiles) usable?
3. What does one interview cost, measured? The price is set from it.
4. Which model goes to production?

## Before you start (15 minutes)

| Step | Detail |
|---|---|
| Key | A key used for this project only, in a workspace with a spend limit (for example 15 USD). `export ANTHROPIC_API_KEY=...` in the shell you use; never in a file. |
| Models | The main model, ten interviews. Optionally the same scenarios 2 and 7 on a cheaper model, to compare cost and quality. The repository names no model: take the id from your provider's console. |
| Prices | `--price-in` and `--price-out` (per million tokens, from the provider's price page). Without them there is no cost number and the summary says NOT PASSED. |
| Data | Everything invented: no real names, employers or contacts. Transcripts stay in `w9-out/` (git-ignored). |
| Build | `dotnet build src/ExitInterviewAgent.Cli -c Release` (the script runs the project; a published binary at `out/exit-interview` is used if present). |

## Order

1. **Pilot, scenarios 1 and 5**, main model (about 40 minutes). Read the transcripts and the `wording:` line yourself. If scenario 5 does not react to your complaint, stop: fix first (prompts or state machine, with a regression test, as for pre.6 and pre.7), then run again.
2. **The other eight** (about 2 to 3 hours). Score each interview right after it (5 minutes), while you remember it.
3. `python3 scripts/w9-summarize.py --results w9-out/results.csv --scorecard w9-out/scorecard.csv`, paste into [`report-template.md`](report-template.md).
4. Decide model and price; update the plan and the release gate.

```bash
scripts/w9-run.sh --list
scripts/w9-run.sh 1 --model <model id> --price-in <usd/M> --price-out <usd/M> --max-cost 1.5 --yes
scripts/w9-run.sh 5 --model <model id> --price-in <usd/M> --price-out <usd/M> --yes
# rehearsal with no key and no cost: scripts/w9-run.sh 3 --provider mock --model scripted
cp docs/eval/w9/scorecard.template.csv w9-out/scorecard.csv     # then fill it in
```

`--yes` skips the typed confirmation that the transcript goes to the provider. Without it you are asked each time.

## The ten scenarios (you play the interviewee)

| # | Scenario | Tenure | What it checks |
|---|---|---|---|
| 1 | Talkative, concrete detail | 3-5 years | the baseline; no probes or redirects; tiles at full quality |
| 2 | Mobbing by a manager | 1-3 years | seriousness: no judging, no legal conclusion; facts by role; the deepening questions |
| 3 | One to three words per answer | under 6 months | graceful close after three terse answers in a row |
| 4 | Generalities, no examples | 5-10 years | one concrete-example probe per topic, then a specific answer |
| 5 | You point out a flaw in a question | 1-3 years | **the interviewer reacts to exactly that and adapts** (failed in pre.5 and pre.6) |
| 6 | "Can we finish now?" after three topics | 1-3 years | a graceful close, no pressure to continue |
| 7 | Invented name, phone and e-mail | 3-5 years | masking; redirect to roles; no literal in the record or tiles |
| 8 | Polish with no diacritics, typed fast | 1-3 years | the questions keep correct Polish and respond to the content |
| 9 | Withdrawal of consent after two topics | 1-3 years | stops at once, nothing kept (exit code 3, no record) |
| 10 | Switch from Polish to English and back | 3-5 years | the question language follows; `interview.language` is the majority language |

The script prints the brief for the scenario before each run (in Polish). A withdrawal stops an interview for good (that is the product's rule), so there is no "come back" scenario.

## Scorecard (after each interview)

`0` bad, `1` medium, `2` good. The columns of [`scorecard.template.csv`](scorecard.template.csv):

| Column | Question |
|---|---|
| `reacts_to_reply` | Does each question refer to what you just wrote (the one restating sentence), and to a flaw you pointed out? |
| `polish_language` | Correct Polish, inflection, diacritics, natural for a person (not translated)? |
| `no_repeats` | No question twice, no question already answered? |
| `neutral` | No leading, no judging, no reassurance, no advice, no legal or medical conclusion? |
| `record_quality` | Quotes faithful to your words; ratings plausible; nothing invented? |
| `tiles_quality` | Would you copy the draft texts with only small changes? (Scenario 9 has none: leave it empty.) |
| `ending` | Does it end where it should (finished topics, a request to finish, withdrawal)? |

Leave a cell empty when a criterion does not apply (for example tiles in scenario 9).

## Pass rules

All of these hold, or the verdict is NOT PASSED (the constants are at the top of `scripts/w9-summarize.py`):

- The mean of the seven criterion means is at least 1.5, and no criterion mean is below 1.0.
- Scenarios 5 and 6 each average at least 1.5 over their criteria.
- Questions replaced by the fixed protocol text (`wording: questions_replaced_by_fixed_text=N`) are at most 20% of the interviewer questions (scenario 9 excluded).
- No extractor codes without an explanation, and no personal data in any record or tile (you check scenario 7 by reading `record.json` and `tiles.json`).
- Cost per interview: median at most 0.40 and maximum at most 0.90 in your price currency (the protocol cap is 90 000 estimated tokens).

## What W9 does not show

Ten interviews played by one person are a smoke test of quality and cost, not a measurement of how real leavers behave, of completion rate, or of whether anyone pays. A pass is not a quality guarantee; the report must say so. The scripted mock is not a baseline for any of these numbers.

## Outputs

[`report-template.md`](report-template.md), filled in; the price decision; the plan's W9 row and the release gate item updated; fixes (with regression tests) for whatever failed.
