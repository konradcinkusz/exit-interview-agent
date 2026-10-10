# Interview v2: Polish, a responsive interviewer, and platform tiles

Status: **implemented** (ADR-0075), all five tasks merged to `main`. **No real-model evaluation:** every behaviour below was exercised with the scripted mock, fake transports and the offline eval harness. Whether the Polish wording, the deepening questions and the tile texts are good with a real model is **not measured** (see [What was measured and what was not](#what-was-measured-and-what-was-not)).

## Why

The first real run showed three problems the owner named:

1. The interview is English only. A person who writes Polish gets English questions.
2. The interviewer feels mechanical. It asks the fixed topic questions, allows one probe, and does not react to what was said: a statement such as "I was bullied at work" gets the same single follow-up as a vague "it was fine".
3. The tiles are generic. The owner wants, at the end of every interview, ready drafts in the shape of real places people post: a Glassdoor entry, a Google review and a Reddit post, where the length and depth differ by platform.

## Decisions (ADR-0075)

**Language.** A Polish protocol file sits next to the English one (`interview-protocol.pl.v1.json`: opening, acknowledgements, probes, topic questions, closings). `--language pl|en|auto`; `auto` (default) starts from the system UI language (`pl` if Polish, else `en`) and switches when the interviewee writes in the other language or asks for it ("po polsku", "in English"). The switch applies from the next question; the record's `interview.language` is the language of most interviewee turns. Cue lists (withdrawal, consent, vague, hostile, naming a person, injection) and the question guard (leading, double-barrelled) get Polish rules; all code-owned decisions stay code-owned.

**A responsive interviewer, inside the structure.** The model still only words questions; the state machine still decides. New: a **serious-account signal** (a deterministic cue list, Polish and English: bullying/mobbing, harassment, discrimination, threats, retaliation, unsafe conditions, wage theft, and similar). When a reply carries it, the topic enters a **deepening phase**: up to `maxDeepProbesPerTopic` (default 4) neutral, fact-seeking follow-ups from a fixed menu (what happened; roughly when and how often; who, by role only; what the interviewee did and how the employer responded; how it ended and what it meant for them), each worded from what was actually said. Each step may open with at most one short sentence that reflects the interviewee's own words, with no judgement, no reassurance, no legal or medical conclusion and no suggestion what to say. After the first deep step the interviewer says once that they can skip or stop at any time. Names are still not recorded (redirect to roles). The protocol moves to 1.2; limits grow (`maxInterviewerTurns`, token and call budgets) and the eval harness limits and baseline move with it (ADR-0040 baseline justification).

**Tiles at the end, always, per platform.** `interview` generates tiles after a record exists, with the same provider the user already chose and consented to (an extra call, counted in the budget; `--no-tiles` exists for scripts). New kinds: `Glassdoor` (a short entry: pros, cons, advice), `GoogleReview` (short), `Reddit` (long, first-person, concrete). Length limits per kind are design limits for drafts, not claims about the platforms' rules, which change: the notice tells the user to check them. When the transcript is still in memory (same session) the writer receives the **PII-masked transcript** as well as the record, because a long narrative cannot be written from five short quotes per topic; run separately from a saved record, tiles use the record only, as before.

**Banned terms become tiered.** Always banned, every tile: allegations of crimes or illegality stated as fact (illegal, fraud, corruption, theft and Polish equivalents), health, protected characteristics as attributes of a named or identifiable person. Allowed only in `Reddit`, and only when the interviewee used the term themselves and the sentence is framed as their own experience (first-person hedge such as "in my experience", "I felt", "moim zdaniem", "odczuwałem"): mobbing, harassment, discrimination, bullying. Not allowed in `Glassdoor` and `GoogleReview`, which are short, easy to search and anchored to a company name. The company name is never filled in: tiles carry the token `[COMPANY]` / `[FIRMA]` for the person to replace, or not.

**Limits we accept.** The deepening cue list is lexical and will miss paraphrases and Polish inflections it does not list. The Polish protocol and guard rules have a small test corpus and no real-model evaluation. Whether the wording is good with a real model is unmeasured. A long public post about an employer carries legal risk that the notice names and the program cannot remove.

## Tasks

| ID | Wave | Title | Files it owns | Depends on | PR | Status |
|---|---|---|---|---|---|---|
| plan | 0 | This document and ADR-0075 | `docs/` | none | [#33](https://github.com/konradcinkusz/exit-interview-agent/pull/33) | merged |
| Y1 | 1 | Polish protocol, `--language`, language-aware prompts | `Protocol/*`, `Roles/Prompts.cs` (language lines only), `Cli/InterviewCommand.cs` (flag), tests | none | [#34](https://github.com/konradcinkusz/exit-interview-agent/pull/34) | merged |
| Y3 | 1 | Platform tiles: kinds, per-kind limits, tiered guard, transcript input, writer prompts, mock, renderer | `Agent/Tiles/*`, `Agent/Mock/*` (tile role only), `schemas/tile-writer-output.v1.schema.json`, `Cli/Tiles/*`, tests | none | [#35](https://github.com/konradcinkusz/exit-interview-agent/pull/35) | merged |
| Y2 | 2 | Polish cues, question guard in Polish, serious-account signal, deepening phase, reflective sentence, protocol 1.2, eval limits and baseline | `Machine/*`, `Roles/QuestionGuard.cs`, `Roles/Prompts.cs`, `Protocol/*.json`, `Eval/Layer1/*`, `evals/baseline.json`, tests | Y1 | [#36](https://github.com/konradcinkusz/exit-interview-agent/pull/36) | merged |
| Y4 | 3 | `interview` ends with tiles (auto), mid-interview language switch, `--no-tiles` | `Cli/InterviewCommand.cs`, `Agent/Runner/*`, tests | Y1, Y2, Y3 | [#37](https://github.com/konradcinkusz/exit-interview-agent/pull/37) | merged |
| Y5 | 4 | Docs, guide chapter and exercise, README, ADR notes, evidence (this document's measurements, RESULTS, release gate item) | `docs/`, README | Y4 | [#38](https://github.com/konradcinkusz/exit-interview-agent/pull/38) | in review |

Y5 adds no Polish eval scenarios beyond the three deepening scenarios Y2 already added (`hap-003`, `con-003` in Polish; `hap-004` in English); the scenario count is 30, not 27 (see below).

Definition of done per task: `dotnet build -warnaserror`, `dotnet test` of the touched projects, `dotnet format --verify-no-changes`, `scripts/run-evals.sh` where behaviour changes, no AI model identifier in any file, commit or PR text, tests written first, a PR per task merged when all CI checks are green.

## What was measured and what was not

Measured on 2026-10-10 on `claude/y5-v2-docs` (from `main` at `4415623`, all five tasks merged), with the commands shown. Nothing here was measured with a real model.

**Build and tests.** `dotnet build -warnaserror` passed with 0 warnings. Each test project was run on its own with `dotnet test <project> --no-build`:

| Project | Passed | Skipped |
|---|---|---|
| Agent | 826 | 0 |
| Cli | 207 | 0 |
| Eval | 161 | 0 |
| InterviewService | 359 | 15 (PostgreSQL only) |
| Personas | 18 | 0 |
| Privacy | 184 | 0 |
| Providers | 174 | 2 (`Category=Live`) |
| Records | 110 | 0 |
| Signals | 102 | 0 |
| **Total** | **2141** | **17** |

**Evaluation harness.** `scripts/run-evals.sh` (validate, gate, report, calibrate) exited 0. The gate ran 54 runs over 30 scenarios (happy 4, ambiguity 4, hostile 2, adversarial 8, degradation 9, consent 3), evaluated 562 constraint assertions with 0 harness errors, and the 12 hard constraints held in every run. Behaviour metrics on the mock profile include coverage 156/156, leading-question rate 0/126 and double-barrelled questions 0/126. The full figures, with the calibration numbers, are in [RESULTS.md §1](../research/RESULTS.md#1-evaluation-harness-mock-profile).

**Mutation proof.** `python3 scripts/mutate-agent.py` weakened one protection at a time in the Agent project (14 mutations, including the two Y2 deepening mutations M-13 and M-14). **14 of 14 were caught** by an assertion in the gate. The tree was clean afterwards.

**Scripted interview in Polish.** `interview --provider mock --model scripted --language pl --tenure 1y_3y --out <dir>` with piped answers completed, wrote `record.json` (valid, submittable) and `tiles/tiles.json` and `tiles/tiles.html`. Two of six tile candidates were dropped by the guard with code `pii_found` (the mock's wording, not a finding about real text).

**Not measured.**

- Whether a real model asks good deepening questions, reflects the interviewee's words without judgement, or keeps to the rules for Polish. The mock understands nothing.
- Whether the tiles written by a real model are good, pass the guard often enough, or read well in Polish or as Reddit-style text.
- The serious-account cue list: its recall and precision on real speech. It is a word list; paraphrases and most Polish inflections are missed, and a word used harmlessly can fire it.
- The language switch on real replies. It is a word count with a short word list, tested on short examples.
- Token cost and budget use with a real model. The mock's +16.9 % mean token rise (protocol 1.2) is a prompt-size fact, not a measure of answer quality.
- Any provider call. No key was used and no network call to a model was made in this work.
- The legal exposure of publishing a tile. The notice and the README say so; the program cannot assess it.
