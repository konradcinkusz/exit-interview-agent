# Interview v2: Polish, a responsive interviewer, and platform tiles

Status: **planned** (ADR-0075). Nothing here is implemented yet; update the status column as each task lands.

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

| ID | Wave | Title | Files it owns | Depends on |
|---|---|---|---|---|
| Y1 | 1 | Polish protocol, `--language`, language-aware prompts | `Protocol/*`, `Roles/Prompts.cs` (language lines only), `Cli/InterviewCommand.cs` (flag), tests | none |
| Y3 | 1 | Platform tiles: kinds, per-kind limits, tiered guard, transcript input, writer prompts, mock, renderer | `Agent/Tiles/*`, `Agent/Mock/*` (tile role only), `schemas/tile-writer-output.v1.schema.json`, `Cli/Tiles/*`, tests | none |
| Y2 | 2 | Polish cues, question guard in Polish, serious-account signal, deepening phase, reflective sentence, protocol 1.2, eval limits and baseline | `Machine/*`, `Roles/QuestionGuard.cs`, `Roles/Prompts.cs`, `Protocol/*.json`, `Eval/Layer1/*`, `evals/baseline.json`, tests | Y1 |
| Y4 | 3 | `interview` ends with tiles (auto), mid-interview language switch, `--no-tiles` | `Cli/InterviewCommand.cs`, `Agent/Runner/*`, tests | Y1, Y2, Y3 |
| Y5 | 4 | Docs, guide chapter and exercise, README, ADR notes, Polish eval scenarios | `docs/`, `evals/scenarios/*`, README | Y4 |

Definition of done per task: `dotnet build -warnaserror`, `dotnet test` of the touched projects, `dotnet format --verify-no-changes`, `scripts/run-evals.sh` where behaviour changes, no AI model identifier in any file, commit or PR text, tests written first, a PR per task merged when all CI checks are green.
