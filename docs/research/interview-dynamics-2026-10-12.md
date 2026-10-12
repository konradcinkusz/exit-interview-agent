# Interview dynamics: why the interview feels wooden, and what to change

Date: 2026-10-12. Status: **analysis and decisions, nothing implemented.** The build plan is
[`../architecture/interview-dynamics-spec.md`](../architecture/interview-dynamics-spec.md). Nothing here was measured with a real model. The diagnosis
comes from reading the code at `f48890a`, plus one transcript rebuilt by hand from the scripted mock's rules. The owner's report of a
real run ("drętwy", wooden and not dynamic) is the trigger. We have no access to that transcript.

Claim labels used below: **[code]** means read in the source at the path given. **[derived]** means it follows from the code but was not run;
the .NET SDK is not installed in the session that wrote this, so no `dotnet` command was run. **[evidence]** is an empirical study.
**[practice]** is practitioner guidance or opinion. **[decision]** is this document's choice.

## 1. Summary

The interview is a fixed six-question survey with a chatbot face. The program decides everything: topic order, at most one follow-up
per topic, a fixed deepening menu. The model is asked to wrap one stock sentence ("restate what they said") around the next scripted
question, and a strict lexical guard throws away anything unusual and falls back to the scripted text. The person hears about six
topics in protocol order. Each comes as an impersonal, translated-sounding Polish question with a formulaic "you said X" in front.
Nothing follows what they actually care about. The interview never warms up, never asks what would have kept them, and never asks
"what did I not ask". The UI tells them they are on "topic n of 6".

The fix keeps the deterministic state machine, which is a design asset for privacy, injection resistance and evaluation, but changes
the policy it runs:

- a short warm-up, then the reason for leaving first;
- topic order and depth driven by what the person brings up (a deterministic salience planner);
- two kinds of topic: core topics get up to two follow-ups of different kinds (example, laddering, consequence), and light topics get one
  1–5 scale question with a follow-up only on an extreme;
- a "what would have kept you" turn and an "anything I did not ask" turn;
- a soft length budget with a visible early exit that keeps the record;
- a register the person picks (Pan / Pani / "na Ty"), so Polish verbs can be natural;
- reflections that are selective and varied, not one stock sentence on every turn;
- a guard that judges the question sentence rather than the reflection, so good questions stop falling back to fixed text;
- safety branches for distress and legal threats.

Estimated cost stays inside the W9 limits at the illustrative prices in §7, but that is an estimate, not a measurement.

## 2. Diagnosis: why it feels wooden

### 2.1 Structure

| # | Behaviour | Where | Effect |
|---|---|---|---|
| D1 | **Fixed topic order.** `Advance()` increments `_topicIndex` through `protocol.Topics` in file order: onboarding, management, growth, pay, culture, reason for leaving. Rule R03 says it outright: "Sześć tematów w stałej kolejności, jedno pytanie naraz." | [code] `Agent/Machine/InterviewMachine.cs` `Advance`; `Protocol/interview-protocol.pl.v1.json` R03 | The thing most leavers want to talk about, why they left, comes last, after five topics they may not care about. A person who opens with "the pay was a lie" is still asked about onboarding first. |
| D2 | **One follow-up per topic, only when vague or short.** `maxProbesPerTopic: 1`. The probe fires only on `s.Vague \|\| s.Short`. | [code] `InterviewMachine.OnAnswer` | A rich, emotional answer gets no follow-up at all ("specific" means advance). That is the opposite of what a human interviewer does: the interesting answers are the ones to dig into. |
| D3 | **Only one kind of follow-up.** The probe seed is always "Jaka jedna konkretna sytuacja albo moment najlepiej to ilustruje?", and the guard *requires* the words example / konkretn / sytuacj / moment in any probe (`probe_without_example`). | [code] protocol `probe`; `Roles/QuestionGuard.cs` `ExampleRequest` | No "why did that matter", no "what did that change for you", no "what happened next". Every follow-up sounds the same. Any other kind of probe a model writes is rejected and replaced. |
| D4 | **Every topic gets the same weight.** All six topics get a full open question, whether or not the person cares. | [code] `Advance` | A long interview with dead turns. Survey research links length to drop-off and shorter, flatter later answers (§3.4). |
| D5 | **No warm-up, no wrap-up.** Consent is followed straight by "Zacznijmy od pierwszych tygodni…". The close is a fixed thank-you. There is no "what would have kept you", no "anything I did not ask" and no summary. | [code] protocol `topics[0]`, `closings` | No rapport building. It misses the two questions HR practice values most (§3.1). |
| D6 | **Fixed length.** The length follows from six topics plus probes. The only exits are the budget, a terse streak, hostility or a finish request. The person is never told roughly how long it takes, or that finishing early keeps what they said. | [code] `InterviewMachine`, protocol `opening` | No sense of pace or control. |
| D7 | **A misfiring serious-account trigger.** `SeriousCue` includes `zwolni\w*`, `wyrzuc\w*`, `fired` and `laid off`. A plain "zwolnili mnie" opens the deepening menu, which includes "Jakie role były w to zaangażowane…" and "Jak firma zareagowała, gdy to zgłoszono?". | [code] `Machine/ReplySignals.cs` `SeriousCue`; protocol `deepeningSeeds` | An ordinary layoff gets up to four interrogation-style questions, one of which assumes something was reported. That is a leading presupposition and makes no sense for a layoff. |
| D8 | **The UI frames it as a survey.** The chat header shows "topic n of 6": `TOPIC_TOTAL = 6` and `topicNumber` counts `topic` turns. | [code] `web/app/app/interview/state.ts`, `ChatPanel.tsx` | It confirms the feeling of filling in a form. |

### 2.2 Wording

| # | Behaviour | Where | Effect |
|---|---|---|---|
| D9 | **Impersonal, translated Polish.** The topic seeds avoid gender by using impersonal constructions: "Jak na co dzień wyglądało kierowanie pracą?", "Jak można opisać sposób, w jaki ludzie tam ze sobą współpracowali?", "…w porównaniu z tym, czego oczekiwano, gdy dołączano do firmy lub awansowano?", "Co doprowadziło do decyzji o odejściu?". | [code] protocol `topics` | It reads like an official form translated from English. The prompt also says "use gender-neutral wording, or Pan/Pani", which produces "Pan/Pani" slashes and more impersonal forms. |
| D10 | **The same reflection, every time.** Both system prompts say "After the interviewee's first answer, ALWAYS open with ONE short sentence that reflects the main point of their last reply". Both user prompts repeat "begin with ONE short sentence in which you restate…". | [code] `Roles/Prompts.cs` `InterviewerSystem`, `InterviewerUser`, `ProberUser`, `DeepProberUser` | A mandatory restatement on every turn becomes a verbal tic ("Rozumiem, że… Słyszę, że…"). Motivational interviewing advice is to reflect *selectively* and to vary simple and complex reflections (§3.3). Mechanical paraphrase is a known marker of a bot. |
| D11 | **The model is told to keep the scripted question.** "Keep the meaning of the question in SEED… Do not add new topics." The model only paraphrases a fixed next question and cannot build on what was said. | [code] `Prompts.InterviewerUser` | The reflection and the question do not connect: "You said your manager ignored you. How would you describe how people worked together?" |
| D12 | **The guard discards natural questions.** `QuestionGuard.Check` runs `SecondInterrogative`, `Assumptive`, `Loaded` and `ClosedStarter` over the *whole* output, reflection included. A reflection that quotes the person ("pisze Pan, że »pewnie i tak nic by nie zmienili«") hits `pewnie`. A reflection with "… i co gorsza …" hits `\b(i)\s+(co)\b` and is read as double-barrelled. Any rejection replaces the turn with the bare SEED. | [code] `Roles/QuestionGuard.cs`; `Runner/InterviewRunner.cs` `WordAsync` (`var text = seed`) | Under a real model, some turns fall back to the stiff protocol text, with no reflection, in between model-worded ones. W9 already counts this (`questions_replaced_by_fixed_text`, pass limit 20%). Commit `7675c62` records an earlier real run in which *every* question was the fixed text. |
| D13 | **Prefaces stuck on the front.** Frustration and the deepening reminder are fixed sentences put in front of the model's text (`$"{wording.AckFrustration} {text}"`), and so is the language-switch confirmation. | [code] `InterviewRunner.WordAsync` | Two or three stacked sentences with a shift of voice in the middle. |
| D14 | **The opening is a legal notice.** About 130 words of disclosure, with "sześciu tematów" in it, then "Proszę odpowiedzieć tak lub nie." | [code] protocol `opening` | Cold, like a form. Every disclosure in it is required and stays (§5). The order, length and warmth can change. |
| D15 | **The AI speaks as a man.** "To wyczerpuje wszystko, o co chciałem zapytać." | [code] protocol `closings.allTopicsCovered` | Minor, but inconsistent with a neutral assistant. |

### 2.3 What the scripted mock shows

The scripted mock (`Agent/Mock/ScriptedChatClient.cs`) answers the interviewer and prober roles with the `SEED:` line, word for word
[code]. So with the mock, every question is the protocol text and nothing reflects. Rebuilt from the code [derived], a cooperative
Polish interview with no serious cue goes like this. The interviewee lines are illustrative.

```text
AI:  Dzień dobry i dziękuję za poświęcony czas. Zanim zaczniemy, trzy sprawy. … Czy zgadza się Pan/Pani kontynuować? Proszę odpowiedzieć tak lub nie.
Ty:  tak
AI:  Zacznijmy od pierwszych tygodni. Jak wyglądało dołączenie do firmy i wejście w rolę?
Ty:  W sumie ok, ale od początku było widać, że pensja nie będzie taka jak obiecano.
AI:  Jak na co dzień wyglądało kierowanie pracą?                         <- the pay remark is ignored (answer was "specific")
Ty:  Różnie.
AI:  Jaka jedna konkretna sytuacja albo moment najlepiej to ilustruje?
…    (growth, pay, culture in file order)
AI:  Co doprowadziło do decyzji o odejściu?                              <- question 6 of 6, about 12 turns in
AI:  To wyczerpuje wszystko, o co chciałem zapytać. …
```

This is the wooden skeleton a real model is asked to decorate. The eval harness passes it (coverage 156/156, leading-question rate
0/126; see [RESULTS §1](RESULTS.md#1-evaluation-harness-mock-profile)), because no metric rewards following the person.
The `fuv`/`opr` pair rewards probing *vague* answers and penalises probing specific ones (`opr`, [SPEC §7](../eval/SPEC.md#7-metrics-counter-metrics-and-confidence)).
That pushes against digging into rich answers.

### 2.4 What is good and stays

- The model never chooses the next step. Interviewee text reaches the machine only through code signals. This is why injection, consent and
  privacy constraints C-01..C-13 hold and can be tested ([SPEC §3](../eval/SPEC.md#3-hard-constraints-layer-1-must-hold-in-100-of-runs)).
- The no-leading-question guard and the neutral, non-judging stance. Both are correct and evidence-backed (§3.6).
- Masking on arrival, the data block with a nonce, verbatim quotes, and no affect field.
- The deepening of serious accounts as an idea. It needs better precision (D7) and trauma-informed wording (§3.5).

## 3. Evidence and practice

**Access limits:** all sources below were accessed 2026-10-12. Direct page fetches from this session failed with DNS errors for every host
except `anthropic.com`. So every row except the Anthropic one is **"seen in search results, not opened"**: the URL and a snippet were seen,
not the full text. Figures quoted from snippets must be checked against the primary text before anyone relies on them. This is an open
item (§8).

### 3.1 Exit interviews

| Source | Finding | Kind |
|---|---|---|
| Spain, E. & Groysberg, B., "Making Exit Interviews Count", *Harvard Business Review*, Apr 2016. <https://store.hbr.org/product/making-exit-interviews-count/R1604G> (not opened) | Most programmes fail on data quality and follow-through. Recommends a semi-structured interview: standard questions for trends plus open probing for surprises. | [practice], based on an executive survey |
| Lefkowitz & Katz (1969) and Estes (1960), as summarised in the SAGE *Encyclopedia of Career Development*, "Exit interview": <https://sk.sagepub.com/ency/edvol/embed/careerdevelopment/chpt/exit-interview>; and a König-group review manuscript, Universität des Saarlandes: <https://www.uni-saarland.de/fileadmin/upload/lehrstuhl/koenig/PDFs-Publikationen/ms_exitint_final.pdf> (both not opened) | Reasons given at exit often differ from reasons given months later. The snippet figure is 59%. Answers drift toward socially safe reasons such as pay. Leavers hold back for fear of retribution and to protect a reference. | [evidence], classic studies cited second-hand |
| SHRM, "12 Exit Interview Questions Every HR Professional Should Ask": <https://shrm.org/topics-tools/news/talent-acquisition/best-exit-interview-questions> (not opened) | Ask about the relationship with the supervisor. Include "what could have been done to keep you". | [practice] |

**Design consequences [decision].**
- The first reason given may be the safe one, so ladder gently behind it once ("what was behind it"), never twice.
- Ask "what would have kept you" explicitly.
- Keep a semi-structured core with adaptive probing.
- An AI interviewer that is not the employer, keeps no names and is not linked to the account removes some of the retribution motive.
  Say so plainly; this is already in the opening.

### 3.2 Probing techniques

| Source | Finding | Kind |
|---|---|---|
| Flanagan, J. C. (1954), "The critical incident technique", *Psychological Bulletin* 51(4). <https://pubmed.ncbi.nlm.nih.gov/13177800> (not opened) | Ask for specific observed incidents rather than general opinions. | [evidence], foundational method |
| Reynolds & Gutman (1988), laddering, *J. Advertising Research*, as summarised at <https://en.wikipedia.org/wiki/Ladder_interview> (not opened) | Repeated "why is that important to you" moves from attribute to consequence to value. Used carelessly it feels like an interrogation. | [practice] |
| Rubin & Rubin, *Qualitative Interviewing* (responsive interviewing), ch. 8, course copy: <https://www.lakeheadu.ca/sites/default/files/uploads/53/dowsley/ANTH2517/Rubin%20Interview%20Structure%20-%20Chapter%208.pdf> (not opened) | Probe types: elaboration, clarification, continuation/echo, plus asking for examples. Probes signal the depth wanted. | [practice] |
| Willis, G. B. (2005), *Cognitive Interviewing*, Sage; Willis, DeMaio, Harris-Kojetin (1999), BLS paper <https://www.bls.gov/osmr/research-papers/1999/pdf/st990240.pdf> (not opened) | Pretest wording with think-aloud and verbal probes to find questions people read in different ways. | [practice] |

**Design consequences [decision].**
- A small, named menu of follow-up *moves*: example (critical incident), elaboration, ladder ("what made that matter"),
  consequence ("what did it change"), and clarification (exists).
- At most two follow-ups per core topic, never the same move twice in a row, and at most one ladder per interview topic.
- Pretest the Polish seeds with five people reading them aloud before W9. This is cheap and needs no model; the owner can do it.

### 3.3 Rapport and reflective listening

| Source | Finding | Kind |
|---|---|---|
| Miller & Rollnick, *Motivational Interviewing* (3rd ed., 2013), OARS, summarised by ISSUP: <https://www.issup.net/node/5871> (not opened) | Open questions, Affirmations, Reflections, Summaries. Reflections are selective. Summaries work at transitions. | [practice] |
| Moyers, Manuel & Ernst, *MITI 4.2* coding manual: <https://www.motivationalinterviewing.org/sites/default/files/miti4_2.pdf> (not opened). Thresholds as cited in BMC Family Practice 2018: <https://bmcfampract.biomedcentral.com/articles/10.1186/s12875-018-0742-5/tables/5> (not opened) | Reflection-to-question ratio of 1:1 is "fair" and 2:1 is "good" in counselling. | [evidence], validated coding instrument. Counselling, not interviewing: used as a direction, not a target |
| Xiao, Zhou, Chen, Yang, Chi (2020), "If I Hear You Correctly: Building and Evaluating Interview Chatbots with Active Listening Skills", CHI 2020. <https://arxiv.org/abs/2002.01862> (not opened) | Chatbots with active-listening skills were rated better and drew better answers. The details were not verified in this session. | [evidence] |

**Design consequences [decision].**
- Reflect when there is something worth reflecting: a judgement, a problem, a strong statement. Skip it after a short neutral answer.
- Prefer one short *complex* reflection (meaning or consequence) over parroting.
- Quote at most a few of the person's own words in „…” when the exact phrase matters.
- Give a one-line summary at the transition to the closing.
- We do **not** adopt the 2:1 ratio. An interview is a question-led genre, and a reflection with no question stalls a text chat.
  Our target is a reflection on at least half of the turns that follow a substantive answer.

### 3.4 Length, pacing and fatigue

| Source | Finding | Kind |
|---|---|---|
| Galesic, M. & Bosnjak, M. (2009), "Effects of questionnaire length on participation and indicators of response quality in a web survey", *Public Opinion Quarterly* 73(2). Summary by Angus Reid (2024): <https://angusreid.com/wp-content/uploads/2024/09/The-Hidden-Costs-of-Long-Surveys-Why-Survey-Length-Impacts-Data-Quality.pdf> (not opened) | The longer the announced length, the fewer people start (snippet: 75% at 10 min, 62% at 30 min). Answers late in a survey are faster, shorter and more uniform. | [evidence], randomised |
| Pew Research Center (2021), "Why do some open-ended survey questions result in higher item nonresponse rates than others?" <https://www.pewresearch.org/decoded/2021/10/14/why-do-some-open-ended-survey-questions-result-in-higher-item-nonresponse-rates-than-others/> (not opened) | Open questions have far higher item nonresponse than closed ones (snippet: about 18% on average vs 1–2%), and the rate varies widely with the question. | [evidence] |
| Anthropic, "About Anthropic Interviewer" (2025). <https://www.anthropic.com/about-anthropic-interviewer> (**opened**) | An AI interviewer that adapts follow-ups and runs in the participant's language. Participants are told how answers will be used before they agree. | [practice], product description |

**Design consequences [decision].**
- Announce a realistic length (about 10–15 minutes) and put the key questions first: reason for leaving, then the person's salient topics.
- Use a scale question for low-salience topics instead of another open question.
- Offer an early exit that keeps the record, once, mid-interview.

### 3.5 Sensitive topics

| Source | Finding | Kind |
|---|---|---|
| SAMHSA (2014), *Concept of Trauma and Guidance for a Trauma-Informed Approach*, SMA 14-4884, via youth.gov: <https://youth.gov/feature-article/samhsas-concept-trauma-and-guidance-trauma-informed-approach> (not opened) | Six principles: safety; trustworthiness and transparency; peer support; collaboration and mutuality; empowerment, voice and choice; cultural, historical and gender issues. | [practice], federal guidance |
| Wrexham University, *Distress Protocol for Sensitive Research* template: <https://wrexham.ac.uk/media/marketing/research/Distress-Protocol-for-Sensitive-Research-Template.pdf> (not opened) | On distress: pause, check safety, offer to stop or continue, signpost support, never delay a referral. | [practice] |
| The HR Specialist, "Exit interviews: use them to cut turnover, unveil legal risks": <https://www.thehrspecialist.com/1210/exit-interviews-use-them-to-cut-turnover-unveil-legal-risks> (not opened) | Employers treat harassment raised at exit like a complaint, so an exit interview is not a confidential channel. | [practice] |
| Polish whistleblower act (Dz.U. 2024 poz. 928), via gov.pl: <https://www.gov.pl/web/rodzina/ustawa-o-ochronie-sygnalistow-przyjeta-przez-parlament> (not opened) | Former employees are covered. Snippets say labour-law matters were removed from the reportable areas. | legal, **needs counsel** |
| Kryzysowy Telefon Zaufania **116 123**, as listed in search results: Niebieska Linia press note <https://www.niebieskalinia.pl/spaw/docs/116123_informacja_prasowa.pdf>; gov.pl/cyfryzacja <https://www.gov.pl/web/cyfryzacja/miedzynarodowy-dzien-walki-z-depresja> (not opened) | Free line for adults in emotional crisis, run with NASK, Niebieska Linia and the Ministry of Digital Affairs. Sources disagree on the hours (14–22 vs 24/7). 112 for immediate danger. | fact, **hours unverified** |

**Design consequences [decision].**
- This product does not report to any employer. It produces a record and drafts for the person. So "the employer must act on it" does not
  apply here, but we must not *imply* that an interview is a report. The legal-threat branch says once that this conversation is not a
  report and not legal advice.
- On explicit distress: a fixed support message with 116 123 and 112, an offer to skip or finish (finishing keeps the record), and no more
  probing on that topic.
- Deepening: drop the presupposing seed ("gdy to zgłoszono"), never ask why something was not reported, and lower the depth to 3.
- The skip/stop reminder stays.

### 3.6 Leading and double-barrelled questions

| Source | Finding | Kind |
|---|---|---|
| Pew Research Center, questionnaire design: <https://www.pewresearch.org/our-methods/u-s-surveys/writing-survey-questions/> (not opened; failed to load) | Ask one thing at a time. Avoid double-barrelled items. Use simple, concrete wording. | [practice] |
| CASRAI, "Leading questions in surveys": <https://casrai.org/guides/leading-questions-in-surveys> (not opened) | Leading questions cue the answer. Loaded questions carry a premise the respondent cannot reject. | [practice] |

**Design consequence [decision]:** keep every guard rule, but apply the question rules to the **question sentence**, and apply only the
naming and leak rules to the reflection, excluding the person's own quoted words (fix for D12).

### 3.7 Polish register

| Source | Finding | Kind |
|---|---|---|
| Kawamoto, Y., Tokyo University of Foreign Studies, corpus and questionnaire study of Polish address forms: <https://tufs.repo.nii.ac.jp/record/2000223/files/lacs30-05.html> (not opened) | "Ty" has spread since 1989 and many speakers view the switch positively. Pan/Pani remains expected by some, in some settings. | [evidence], sociolinguistic |
| *Rzeczpospolita*, "W korespondencji służbowej jest dużo błędów": <https://pro.rp.pl/prawo-w-firmie/art14344981-w-korespondencji-sluzbowej-jest-duzo-bledow> (not opened) | Pan/Pani is the safe default with strangers. | [practice] |
| PAP interview with Prof. K. Kłosińska (Rada Języka Polskiego): <https://www.pap.pl/aktualnosci/prof-klosinska-formy-niebinarne-sa-nowe-i-na-razie-szokuja> (not opened) | Neuter past forms ("zrobiłom") are regular but strike many readers as new and jarring. | [practice] |

**Design consequences [decision].** Polish second-person past tense is gendered (zdecydowałeś / zdecydowałaś; zdecydował Pan /
zdecydowała Pani). The current way out, impersonal forms, is what makes the text sound translated (D9). Instead:

1. The opening offers the choice in the consent line: „tak”, „tak, per Pani”, „tak, per Pan”, „tak, na Ty”. The web shows these as buttons.
   The choice is the person's own declaration. It is not inferred, it is held in session memory only, and it never reaches the record,
   logs or traces.
2. With **Pani** or **Pan** chosen, the model conjugates normally.
3. With **Ty** or with no choice, the model uses natural constructions that avoid gendered past tense: present tense, noun phrases,
   or "Ci/Panu/Pani" with impersonal verbs ("Jak to się stało, że pojawiła się myśl o odejściu?", "Co Ci najbardziej przeszkadzało?"). It
   never uses slashes or neuter forms.
4. The AI never speaks of itself in a gendered past tense ("nie padło pytanie", not "nie zapytałem").

None of this is sourced guidance; it is our design, checked by a native-speaker read in W9.

### 3.8 Chatbot and LLM interviewers

| Source | Finding | Kind |
|---|---|---|
| Xiao, Z. et al. (2020), "Tell Me About Yourself: Using an AI-Powered Chatbot to Conduct Conversational Surveys with Open-ended Questions", *ACM TOCHI* 27(3). <https://arxiv.org/abs/1905.10700> (not opened) | Field study (about 600 participants) comparing a chatbot with a form. Chatbot answers were more informative, relevant, specific and clear, and engagement was higher. | [evidence] |
| Kim, S., Lee, J., Gweon, G. (2019), "Comparing Data from Chatbot and Web Surveys", CHI 2019. <https://www.springerprofessional.de/doi/10.1145/3290605.3300316> (not opened) | Chatbot respondents satisficed less. A casual style helped on the chatbot. A later study did not replicate the chatbot advantage: <https://lirias.kuleuven.be/retrieve/697279> (not opened). | [evidence], mixed |
| Wuttke, A. et al. (2024), "AI Conversational Interviewing: Transforming Surveys with LLMs as Adaptive Interviewers", arXiv: <https://arxiv.org/abs/2410.01824> (not opened) | Small randomised comparison. Quality was comparable to human interviewers. Most failures of the AI were *missing follow-ups* on surprising or unclear answers. | [evidence], small n |
| Chopra, F. & Haaland, I. (2023), "Conducting Qualitative Interviews with AI", CESifo WP 10666. <https://cesifo.org/en/publications/2023/working-paper/conducting-qualitative-interviews-ai> (not opened) | Hundreds of AI-led interviews. The answers were valid and predicted later behaviour, and many participants preferred the AI interviewer. | [evidence], working paper |
| Lucas, G. et al. (2014), "It's only a computer: Virtual humans increase willingness to disclose", *Computers in Human Behavior* 37. Via USC ICT: <https://ict.usc.edu/?p=10267> (not opened) | People disclosed more when they believed no human was operating the agent. | [evidence], lab |
| "The influence of conversational agent embodiment and conversational relevance on socially desirable responding", *Decision Support Systems*. Record: <https://datalearner.com/academic/journal-papers/0167-9236/volumes-and-issues/276/paper-detail/4590> (not opened; authors and year unverified) | More humanlike, capable agents drew *more* socially desirable answers on sensitive items. | [evidence], unverified |

**Design consequences [decision].**
- Conversational adaptivity is worth the effort: response quality improves with it, and the known LLM failure is the *missing*
  follow-up, which is exactly D2.
- Keep the AI disclosure, which may help disclosure.
- Do not make the persona humanlike. No name, no feelings, no small talk, no "I understand how you feel".
  Warmth comes from listening, not from personality.

### 3.9 Evidence and opinion, kept apart

- **Supported by evidence:** conversational chatbots improve the quality of open answers. Length reduces starts and late-answer quality.
  Open questions cost nonresponse. Leavers under-report sensitive reasons. LLM interviewers mostly miss follow-ups. Believing
  "no human is watching" can raise disclosure.
- **Practice or opinion:** OARS and selective reflection, laddering depth limits, trauma-informed steps, register choice, "magic wand"
  and "anything else" questions, and the target numbers in the spec.
- **Unknown and not measured here:** whether any of this works for Polish leavers with the project's chosen model. That is W9's job.

## 4. Decisions

| # | Decision | Reason |
|---|---|---|
| X1 | Keep the code-owned state machine. Change the *policy*: warm-up, then reason first, then salience order, then retention, then "anything else", then close. | Constraints C-01..C-13 stay provable; §2.1, §3.1, §3.4 |
| X2 | A deterministic **TopicPlanner** ranks the remaining topics from lexical cues in the person's answers (PL and EN), ignoring sentences flagged as injection. | C-05 (injection never changes order relative to the control run) remains true by construction |
| X3 | **Core** topics (salient, up to 3 plus the reason) get up to 2 follow-ups from a move menu. **Light** topics get one 1–5 scale turn, and one follow-up on 1–2 or 5. | §3.2, §3.4 |
| X4 | Follow-ups fire on substance (salience, a strong statement, concreteness missing), not only on vagueness. `opr` is redefined accordingly. | D2, §3.8 (Wuttke) |
| X5 | Reflection is selective and varied, and may quote up to 6 of the person's words in „…”. | D10, §3.3 |
| X6 | The guard judges the question sentence. The reflection gets only the naming, PII and leak rules. Text the person wrote, quoted in „…”, is exempt from lexical "leading" rules. | D12 |
| X7 | The person chooses the register in the consent line. The choice lives in session memory only. | D9, §3.7 |
| X8 | Soft length budget: target 14 questions and hard stop 24. A mid-interview check-in offers to finish and keep. The opening states "zwykle 10–15 minut". | D6, §3.4 |
| X9 | The serious-account cue list is split. A dismissal is an *exit event*: one ordinary follow-up, no deepening. Deepening is capped at 3 and the presupposing seed is replaced. | D7, §3.5 |
| X10 | Safety branches: explicit distress gets fixed support text (116 123, 112), the topic is released and no more probing follows. A legal threat gets one fixed "not a report, not legal advice" sentence. Names are redirected as now. | §3.5 |
| X11 | The UI drops "topic n of 6" and shows an elapsed or remaining time hint. | D8 |
| X12 | No API contract, record schema, consent semantics, billing or cost-control change. New turn types map to the existing wire kinds (`topic`, `probe`). | §5 of the spec |

## 5. What must not change

AI disclosure first (C-04). No content before consent (R02). Withdrawal discards everything (C-03). Finish-and-keep. Deletion by receipt.
Masking on arrival and no names (C-01, C-02). Verbatim quotes (C-06). No affect field, and no stored inference of emotion (C-07). No
content in telemetry (C-08). The record schema v1 and its six topics. The web/BFF API contracts (`InterviewContracts.cs`), credits and
billing, and the cost controls. Interviewee text influences flow only through code signals. Details are in the spec, §2.

## 6. Quality targets (summary)

Defined in the spec, §6:

- questions replaced by fixed text ≤ 10% (W9 gate 20%);
- a reflection on ≥ 50% and ≤ 85% of the questions that follow a substantive answer;
- probes on salient topics ≥ 90%, and none on light topics with a mid-scale answer;
- median questions 12–16, terse ≤ 6;
- covered topics with a concrete detail ≥ baseline;
- new W9 criterion `natural_flow` with mean ≥ 1.5;
- cost within the W9 limits.

## 7. Cost

The design adds no model call per turn. It adds about 700 tokens of system prompt per question call, and it lowers the expected number of
questions on low-salience interviews. The spec, §7, has the arithmetic. At the illustrative prices of 3 USD in and 15 USD out per million
tokens, the typical cost is about 0.23 USD per interview and about 0.45 USD in the worst case under the token cap. At 5 USD and 25 USD it is
about 0.39 (at the W9 median limit of 0.40) and about 0.75. **Not measured.** W9 measures it.

## 8. Open items, not verified

- No primary source above was opened except Anthropic's page, because of DNS failures in this session. Re-check the figures marked
  "snippet" before citing them anywhere else.
- The hours of 116 123 (sources disagree), and the scope of the Polish whistleblower act for labour-law matters. Both need checking
  before the texts ship. The support text is written so that it does not state hours.
- Whether lexical salience cues are good enough in Polish (inflection). The planner falls back to protocol order when it finds nothing,
  so it never does worse than today.
- Real-model behaviour of every prompt in the spec. Nothing here was run.
- The owner's wooden transcript was not available. The diagnosis is from code.
