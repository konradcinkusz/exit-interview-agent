# W9 report: real-model run, Polish

Status: **template, not run.** Replace this block with the date, the person who played the interviewee, and the commit of the build used.

| Field | Value |
|---|---|
| Date | |
| Commit / CLI version | |
| Provider and model id (main) | |
| Model id (comparison, if any) | |
| Prices used (per million tokens, in / out, currency) | |
| Spend limit set in the provider's console | |
| Interviewee played by | |

## 1. Verdict

Paste the "Verdict" line printed by `scripts/w9-summarize.py`. Then one sentence: what is good enough to launch, and what is not.

## 2. Results

Paste the output of `python3 scripts/w9-summarize.py --results w9-out/results.csv --scorecard w9-out/scorecard.csv` (cost and wording table, scorecard means, verdict).

## 3. Per scenario

| # | Scenario | Score (avg) | What happened (one line) | Fix needed? |
|---|---|---|---|---|
| 1 | Talkative | | | |
| 2 | Mobbing | | | |
| 3 | Terse | | | |
| 4 | Generalities | | | |
| 5 | Pointing out a flaw | | | |
| 6 | Request to finish | | | |
| 7 | Personal data | | | |
| 8 | No diacritics | | | |
| 9 | Withdrawal | | | |
| 10 | Language switch | | | |

## 4. Quotes (anonymised, invented data only)

Three good and three bad interviewer turns, each with the interviewee turn before it. Say which scenario and why it is good or bad.

## 5. Tiles

For each of Glassdoor, Google and Reddit: how many were shown and how many were dropped (with the code); one tile you would use, one you would not and why. Remember that the texts are proposals, not facts.

## 6. Cost and price

| Item | Value |
|---|---|
| Median cost per interview | |
| Maximum | |
| Mean tokens in / out | |
| Median latency of one interview | |
| Margin used | |
| Proposed `Billing:PriceMinorUnits` and `Billing:Currency` | |

Price = median cost multiplied by a margin that covers failed interviews and refunds, the payment provider's fee, taxes and your time. Say how the margin was chosen. No number is built into the code.

## 7. Model decision

Which model goes to production and why (quality against cost). Set `Parameters:interviews-model` (local) and the Fly secret accordingly.

## 8. Fixes and follow-up

A list: what failed, the fix (PR), the regression test. Anything that needs another run.

## 9. What this does not show

Ten interviews played by one person; one language; the scripted mock is not a baseline; no real leavers; completion rate and willingness to pay are unmeasured.

## 10. Updates made

- [ ] `docs/architecture/web-app-plan.md`: the W9 row and the status line
- [ ] the release gate item for the real-model run
- [ ] price and model set in the deployment configuration (not in the repository)
