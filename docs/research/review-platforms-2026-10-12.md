# Where a former employee can write about an employer: ten platforms, their rules, and a draft profile for each

Date: 2026-10-12. Status: **research, not legal advice.** Implementation spec: [platform-drafts-spec.md](../architecture/platform-drafts-spec.md).
Related: [draft-tiles.md](../architecture/draft-tiles.md), [ADR-0074](../adr/0074-draft-tiles-from-the-record.md),
[ADR-0075](../adr/0075-polish-responsive-interview-and-platform-tiles.md), [legal checklist](../legal/CHECKLIST.md) (L1, L2, L4, and the confidentiality question).

> **How far to trust this page.** Every platform fact below is cited to the platform's **own** domain (help centre, terms,
> community guidelines, developer docs), accessed 2026-10-12. **No page was opened in full.** This environment's egress proxy
> and DNS refused glassdoor.com, indeed.com, kununu.com, comparably.com, gowork.pl, pracuj.pl, support.google.com,
> trustpilot.com, transparency.meta.com, reddit.com, linkedin.com, wykop.pl and x.com, and reddit.com is also excluded as a
> search domain. Each fact comes from a search engine's summary of an indexed copy of the cited official page. In the tables,
> **S** means that kind of summary, and **UNVERIFIED** means no official source supported the fact. A number marked S may be
> out of date: some indexed copies are years old. Before a profile's limits are treated as platform rules, someone with
> normal web access must open each URL and record the date (spec work package WP6). The program's limits are **design
> limits for drafts**, not claims about any platform ([TileLimits](../../src/ExitInterviewAgent.Agent/Tiles/TileContracts.cs)
> already says so).

## 1. The decision in one paragraph

Draft texts become **per service**: each platform has a typed profile that says how much detail is appropriate, how the
draft is structured, what tone it uses, its length, which interview topics feed it, what it must leave out, its risk level
and the warning shown with it. The program still **never posts**: it shows a draft, a copy button per field and a link that
opens the site. **Two platforms the product targets today are dropped.** Google Maps and Trustpilot both forbid reviews by
current or former employees of the business reviewed (S, sections 3.11 and 3.12). The existing `google_review` tile
therefore steers users toward breaking that platform's rules, and it is retired. Ten platforms are chosen, one of which
(Kununu) ships disabled until its rules can be read.

## 2. Selection

Criteria, in order: (1) the platform **allows** a former employee to write about an employer; (2) reach in **Poland/EU** and
the English-speaking market; (3) a distinct norm (a structured review, an open forum, a real-name professional network, a short
post), so that a profile adds something an existing one does not; (4) how much of its rules could be confirmed.

| # | Platform | Kind of text | Identity | Market | Why it earns a place |
|---|---|---|---|---|---|
| 1 | Glassdoor | structured review | anonymous to readers, verified email | global, EN | the reference employer-review site; Fishbowl now lives inside it as "Community on Glassdoor" |
| 2 | Indeed company reviews | structured review | anonymous to the employer | global, incl. pl.indeed.com | largest job board; one review per company |
| 3 | GoWork.pl | free-text opinion | anonymous, IP stored | **Poland** | the Polish "Opinie o pracodawcach" site; Pracuj.pl has no equivalent open review |
| 4 | Kununu | structured review | anonymous | **DACH/EU** | the EU-market counterpart of Glassdoor; **ships disabled**, rules UNVERIFIED |
| 5 | LinkedIn post | professional post | **real name** | global, PL strong | where professionals actually speak about work; no employer reviews (S) |
| 6 | Reddit | long narrative | pseudonymous | EN, plus r/Polska and r/poland | the long, concrete account the owner asked for |
| 7 | Blind | review or post | pseudonymous, **verified work email** | EN, tech | verified employees; company shown with every post |
| 8 | Wykop.pl | forum post (Mikroblog) | pseudonymous | **Poland** | the Polish open forum; a Polish counterpart to Reddit |
| 9 | Facebook groups | group post | **real name** (usually) | **Poland**, global | Polish industry and job groups are a main channel; rules set by each group |
| 10 | X | short post | real name or pseudonym | global | the short public post (today's `short_note` gets a home) |

Considered and **not chosen**:

| Platform | Why not |
|---|---|
| Google Maps / Business Profile | Its conflict-of-interest policy forbids it: "current or former employment" is named as a conflict of interest, and such content is removed (S) [support.google.com/contributionpolicy/answer/7400114]. **The existing `google_review` tile is retired.** |
| Trustpilot | Excludes reviews from "someone who is working for your business" and treats AI-generated content as grounds for flagging (S) [legal.trustpilot.com/for-businesses/guidelines-for-businesses; legal.trustpilot.com/for-reviewers/guidelines-for-reviewers] |
| Pracuj.pl | Employer profiles are run by the employer, and its "Opinie o firmie" look like employer-chosen testimonials. No open, anonymous employee review was found (S, partly UNVERIFIED) [pomoc.pracuj.pl/hc/pl/articles/360039049034-Profil-pracodawcy; pracodawcy.pracuj.pl] |
| Comparably | Rules are fairly clear (S), but it is US-focused, its fields are mostly multiple choice, and it overlaps Glassdoor and Indeed. First candidate if a slot frees up. |
| Fishbowl | Owned by Glassdoor since 2021 and described as "Community on Glassdoor" (S) [glassdoor.com/about/press-release/glassdoor-powers-fishbowl; help.glassdoor.com/s/article/About-Community-on-Glassdoor]. The Glassdoor profile covers it. |
| Welcome to the Jungle, Kariera.pl, Absolvent | Not researched. Whether any of them hosts employee reviews is UNVERIFIED. |

## 3. What each platform's own pages say

Columns: (a) format and fields, length; (b) moderation; (c) individuals, confidential information, defamation, health;
(d) identity; (e) automation, AI content, posting for someone else, incentives; (f) API. All access dates 2026-10-12. All
facts are **S** unless marked otherwise.

### 3.1 Glassdoor

- (a) Pros, cons and "advice to management". Glassdoor encourages giving both a pro and a con without requiring it [glassdoor.com/employers/blog/the-why-behind-glassdoor-review-moderation-process/].
  - Job title is optional but must be accurate if given. The job must be within the last 5 years. Employment status covers contract, freelance and intern work [help.glassdoor.com/s/article/Tips-on-writing-a-review-to-avoid-violating-Glassdoor-s-Community-Guidelines].
  - One review per employer, per year, per review type [help.glassdoor.com/s/article/Community-Guidelines].
  - Headline, sub-ratings, and minimum and maximum review length: **UNVERIFIED**.
- (b) Every review is checked before it goes live. Reviews are rejected for excessive capitals, filler characters or web links, never for being negative or positive [moderation blog above; Community-Guidelines].
  - A rejected review can be revised within 30 days [help.glassdoor.com/s/article/My-review-was-removed].
- (c) Only the highest leadership (C-level or president) may be named, with no negative remarks about other identifiable people and no one's contact information [Tips article above].
  - No confidential or proprietary information, such as source code, customer lists, budgets, M&A details or detailed financials [glassdoor.com/about/trust/when-is-content-removed/].
  - No threats or harassment. Defamation has its own guidance [help.glassdoor.com/article/Tips-on-writing-a-review-to-avoid-defamation/en_US/].
  - Health information: **UNVERIFIED**.
- (d) The user verifies an email address, and employment may be checked [glassdoor.com/about/trust/protecting-user-anonymity/].
  - Glassdoor resists subpoenas but can be forced to disclose identity [help.glassdoor.com/s/article/How-does-Glassdoor-respond-to-legal-requests-or-legal-action-to-find-out-who-posted-a-review].
- (e) No software or automated agents, and no multiple accounts. Whoever adds AI-generated content "adopts, confirms, and ratifies it" [glassdoor.com/about/terms/].
  - Paid or coerced reviews are removed [Community-Guidelines].
  - Posting on someone else's behalf: **UNVERIFIED**.
- (f) Glassdoor "no longer supports API partnerships", and there is no posting endpoint [help.glassdoor.com/s/article/Glassdoor-API].

### 3.2 Indeed company reviews

- (a) Job title, job status and location are required. Star ratings cover work-life balance, pay and benefits, job security and advancement, management, and culture. The free text asks what you liked and did not like [indeed.com/help/job-seekers/articles/360046789111-company-reviews-best-practices-policies-and-guidelines].
  - One review per person per company. A review cannot be edited; an updated review replaces it [indeed.com/help/job-seekers/articles/360026912831-adding-and-deleting-company-reviews].
  - Character limits: **UNVERIFIED**.
- (b) Content is actioned automatically or by human moderators [indeed.com/legal/digital-services-act-transparency-report].
  - Reported reviews that break the rules are removed within 1–2 business days [support.indeed.com/hc/en-us/articles/360046789111].
- (c) Nothing that identifies non-public individuals, no personal or non-public company information, and nothing libellous or defamatory. Reviews are not for reporting crimes or alleging illegal activity [same].
  - Health information: **UNVERIFIED**.
- (d) Anonymous, and the account's details are not shared with the company. Identity can be disclosed under a court order [indeed.com help article above].
- (e) No submitting content "by automated means, in bulk or otherwise" [uk.indeed.com/legal]. Whoever uses AI-generated content "adopts, confirms and ratifies" it [indeed.com/legal].
  - Paid or pressured reviews are removed. Posting for someone else: **UNVERIFIED**.
- (f) No public review API. docs.indeed.com covers jobs and employer data [docs.indeed.com].

### 3.3 GoWork.pl

- (a) Free text. GoWork advises "as many specific remarks as possible" [gowork.pl/faq_uzytkownicy].
  - Length limits and the full regulamin and "Polityka opinii": **UNVERIFIED** (not retrieved).
- (b) Opinions are **not checked before publication**. An opinion is removed or archived (hidden, not deleted) only for breaking the rules or the law: "pomówienia, nieprawdziwe informacje przedstawione jako fakty, naruszenia dóbr osobistych, obraźliwy i wulgarny język" [gowork.pl/poradnik/17/prawo-pracy/jak-usunac-opinie-z-gowork-instrukcja-dla-pracodawcow/].
  - A flagged opinion is archived within 21 days [gowork.pl/faq_uzytkownicy]. Opinions are never removed on request or for payment [gowork.pl/lp/mity-o-gowork].
- (c) "wulgaryzmy, oszczerstwa, pomówienia i dane osobowe mogą zostać usunięte" [gowork.pl/blog/9-krokow-ktore-warto-wykonac-gdy-firma-jest-krytykowana-w-sieci/].
  - GoWork **stores the poster's IP address**. It does not give it to employers, but does give it to law enforcement when an offence is likely [gowork.pl/faq_uzytkownicy; gowork.pl/lp/mity-o-gowork].
  - Court practice, from the court portals:
    - SO Poznań XVIII C 530/17 (2017) held that GoWork was not the right defendant in a personal-rights claim over an anonymous post [orzeczenia.poznan.so.gov.pl].
    - SO Poznań VII Pa 118/20 (2020) held that one short anonymous GoWork post did not justify dismissal without notice [orzeczenia.ms.gov.pl].
    - A Supreme Court ruling on GoWork disclosing reviewer data: **UNVERIFIED** (none found).
    - GoWork pages citing art. 23/24 KC or art. 212 KK: **UNVERIFIED**.
- (d) Anonymous by default; no identifying data required [faq].
- (e) Automation, AI and posting for someone else: **UNVERIFIED** (regulamin not retrieved).
- (f) No API found: **UNVERIFIED**.

### 3.4 Kununu (all UNVERIFIED)

No official kununu page could be fetched, and the search tool refuses the domain. Secondary sources (law-firm articles, not
official) report two things. In 2024 OLG Hamburg ordered kununu to reveal reviewers or delete their reviews; OLG Dresden
(4 U 744/24) upheld a review as protected opinion. **The profile ships with `enabled: false`** until kununu.com/de/info and
help.kununu.com are read.

### 3.5 LinkedIn (posts)

- (a) A post is limited to 3,000 characters, and a group post too. An article allows up to 125,000 [linkedin.com/help/linkedin/answer/a528176; a522483; a544801].
  - No employer-review feature was found; LinkedIn's own advice pages point to Glassdoor, Indeed and Kununu [linkedin.com/help/linkedin/answer/a570559].
- (b) Content is removed after member reports. Egregious violations can restrict an account after one incident [answers a1336329, a1340522].
- (c) No bullying, shaming or disparagement, and no doxxing [linkedin.com/legal/professional-community-policies].
  - Calling out abusive conduct is allowed when it stays on "objective awareness" [answer a1342756].
  - The User Agreement forbids disclosing "confidential information of others, including your employer" [linkedin.com/legal/user-agreement].
  - Defamation follows a separate, country-specific process [answer a1339420].
- (d) **Real name required.** No profile for someone else and no use of another person's account (User Agreement 8.1, 8.2).
- (e) No bots, automated access or scraping. Members are responsible for content they post that was made with AI tools [User Agreement; answers a1341387, a1340567].
- (f) "Share on LinkedIn" (`w_member_social`) lets an app post for a signed-in member [learn.microsoft.com/en-us/linkedin/consumer/integrations/self-serve/share-on-linkedin]. **We will not use it.**

### 3.6 Reddit

- (a) Title plus body. The 300-character title and 40,000-character body limits appear only on third-party pages: **UNVERIFIED**. Each subreddit sets its own rules, and **no subreddit's rules could be read** (reddit.com blocked): **UNVERIFIED** for r/antiwork, r/cscareerquestions, r/jobs, r/Polska and r/poland.
- (b) Moderators can route likely harassment to a queue automatically [support.reddithelp.com/hc/en-us/articles/23856209638932-Harassment-filter].
- (c) Posting someone's personal information, including screenshots with readable names, is not allowed. A CEO is a limited exception [support.reddithelp.com/hc/en-us/articles/360043066452]. The harassment rule does not protect ideas or public figures from criticism [articles/360043071072]. AI content that misleads about real people is banned [articles/41180423371156].
  - Defamation and confidential information: **UNVERIFIED**.
- (d) Pseudonymous usernames. User Agreement: **UNVERIFIED** (not fetched).
- (e) Automated accounts must not pretend to be human. AI-generated content is allowed subject to each community's rules but should be disclosed; content that passes itself off as human-made is banned [articles/41180423371156, 360043512931].
  - The Responsible Builder Policy requires approval for any API access and bans near-identical posts across subreddits [articles/42728983564564].
- (f) A Data API exists, access by approval [articles/16160319875092]. Not used.

### 3.7 Blind

- (a) A company review needs a detailed explanation with pros and cons. A former employee gets **one review per former employer** [help.teamblind.com/article/89-company-reviews].
  - Post length limits: **UNVERIFIED**.
- (b) Reviews are checked by moderators before they appear, and Blind does not edit them [help.teamblind.com/article/108].
- (c) No PII about anyone and nothing that would "de-anonymize, identify, or target individuals". Widely recognised leaders may be discussed for their public conduct [teamblind.com/community-guidelines].
  - No proprietary or confidential information [help.teamblind.com/article/98-company-secrets]. Allegations of illegal activity may be declined [article 89].
- (d) A **verified work email** is required to post. The email is hashed at registration [help.teamblind.com/article/67-how-to-join-blind; us.teamblind.com/setting/term_and_privacy].
- (e) No bots, scraping or manipulation (terms effective 2025-09-08) [us.teamblind.com/setting/term]. AI content and posting for someone else: **UNVERIFIED**.
- (f) No public API found: **UNVERIFIED**.

### 3.8 Wykop.pl

- (a) Mikroblog entries (wpisy) and links (znaleziska). A znalezisko can be edited for 15 minutes. Posting rate depends on account level [wykop.pl/faq/tresci-dodawanie-glosowanie].
  - Character limits: **UNVERIFIED**.
- (b), (c) The moderation standards forbid publishing other people's personal data (names, addresses, linked profiles) and attacks on users, including exposing their identity [wykop.pl/standardy-moderacji].
  - Regulamin clauses on insults and defamation: **UNVERIFIED**.
- (d) Natural persons aged 13 or older only [wykop.pl/regulamin]. Pseudonyms appear to be the norm: **UNVERIFIED**.
- (e) Bots, AI content and account sharing: **UNVERIFIED**.
- (f) An official API exists [dev.wykop.pl]. Not used.

### 3.9 Facebook groups

- (b), (c) The Bullying and Harassment standard applies to Groups. Private individuals get more protection: content that degrades or shames them is removed, and threats to release personal data or medical records are prohibited [transparency.meta.com/policies/community-standards/bullying-harassment/].
- Each group's own rules and admins apply on top: **not researchable** in general.
- Privacy violations, inauthentic behaviour, defamation, identity, and a Groups posting API: **UNVERIFIED**.

### 3.10 X

- (a) A post is up to 280 characters. Paid subscribers can post up to 25,000 [help.x.com/en/using-x/types-of-posts; help.x.com/en/using-x/x-premium].
  - One help page still says 4,000; the API changelog confirms 25,000 [docs.x.com/changelog].
- (c) Sharing someone's private information without consent is prohibited. X leans toward removal when private individuals are targeted [help.x.com/en/rules-and-policies/personal-information; …/public-interest].
  - Defamation policy: **UNVERIFIED**.
- (d) Pseudonymous accounts are allowed; impersonation is not [help.x.com/en/rules-and-policies/x-impersonation-and-deceptive-identities-policy].
- (e) Automating another user's account requires their express consent. Bots must be labelled. No scraping (ToS 2026-04-10) [help.x.com/en/rules-and-policies/x-automation; x.com/en/tos].
- (f) A paid, per-use API exists [docs.x.com/x-api/getting-started/pricing]. Not used.

### 3.11 Google Maps (excluded)

- "A conflict of interest may include **current or former employment** …" Such content is removed. The policies apply "whether it is human-generated or AI-generated" [support.google.com/contributionpolicy/answer/7400114; answer/11414422].
- There is no review-creation API; owners can only reply [developers.google.com/my-business/reference/rest/v4/accounts.locations.reviews].

### 3.12 Trustpilot (excluded)

- Reviews from "someone who is working for your business" are excluded. Incentivised, fake and AI-generated reviews are flagged. AI agents and bots are prohibited [legal.trustpilot.com/for-businesses/guidelines-for-businesses; legal.trustpilot.com/for-reviewers/guidelines-for-reviewers].
- There is no consumer review-posting API [developers.trustpilot.com/service-reviews-api/].

### 3.13 What the platforms share

These rules recur across the chosen platforms:

- **Do not name or describe non-executive individuals.** Glassdoor, Indeed, Comparably, Blind, Reddit, Wykop and Meta all say so; LinkedIn and X say so for private people.
- **No confidential employer information.** Glassdoor, Indeed, Comparably, Blind and the LinkedIn User Agreement all say so.
- **No allegation of crime presented as fact.** Indeed and Blind say so explicitly, GoWork removes "pomówienia", and every site removes defamation.
- **No automated or bulk posting, and no bots.** This is in every terms page that could be read.
- **Whoever posts AI-written content is its author and responsible for it.** Glassdoor, Indeed and LinkedIn say so explicitly. Reddit wants AI content disclosed under community rules.
- **No public API for posting a review** on any review site. (LinkedIn, X, Reddit and Wykop have posting APIs, which we do not use.)

**Consequence for the product: draft, copy, open the site. Never post, never prefill a form, never automate a browser.**

## 4. Draft profiles

The writer works in **four families**, and each profile renders one family draft in its own shape. Why families: the spec
explains it ([§3.3](../architecture/platform-drafts-spec.md#33-generation-one-writer-call-four-families-ten-renderings));
in short, it keeps one model call and the current credit and token budget.

The rules common to every profile come first. Never name a person (stricter than Glassdoor's C-level exception). Never name
the employer: `[COMPANY]`/`[FIRMA]`. No confidential or non-public information, such as customers, figures, code or deals.
No allegation of illegal or unethical conduct stated as fact. No health, disability, pregnancy, family, religion, sexuality,
age, nationality or other protected characteristic, the author's own included. No contact details and no exact dates or team
sizes that would identify a person. **Every draft is a proposal, not a fact, and nothing here is legal advice.**

| Profile | Family | Detail | Structure (labels EN / PL) | Tone | Length (design) | Fed by topics | Extra leave-outs | Risk |
|---|---|---|---|---|---|---|---|---|
| glassdoor | structured | low | Headline / Nagłówek; Pros / Plusy; Cons / Minusy; Advice to management / Rada dla zarządu | neutral, balanced (at least one pro if any topic is ≥3) | headline ≤60; each block 40–500; total ≤1100 | all covered; cons from ≤2, pros from ≥4 | C-level names too (stricter than the site) | medium |
| indeed | structured | low | Title / Tytuł; What I liked / Co mi się podobało; What could be better / Co mogłoby być lepsze | neutral | title ≤60; blocks 40–500 | as glassdoor, no advice block | | medium |
| kununu (disabled) | structured | low | Title; What is good; What is not; Suggestions (labels UNVERIFIED) | neutral | as glassdoor | as glassdoor | | medium |
| gowork | structured, rendered as plain paragraphs | low | no labels (free text): three short paragraphs | very measured, opinion framed as opinion ("moim zdaniem") | 200–1100 | as glassdoor | anything that reads as a factual accusation | **high** |
| linkedin | professional | low–moderate | 1 short opening, 2–4 lines of what was learned or what worked, 1 forward-looking line | professional, constructive, no blame | 300–1300 (site 3000) | what_worked, growth, reason_for_leaving; negatives only as "what I would look for next" | any criticism of identifiable people, any ex-colleague detail | **high** |
| facebook_group | professional | moderate | short intro, what it was like, one tip for others | plain, conversational, not hostile | 300–1300 | all covered | group-identifying detail; screenshots | **high** |
| reddit | narrative | high | Title / Tytuł; Body / Treść (what happened, roughly when, how often, response, effect) | first person, concrete, experience framed as experience | title ≤120; body 600–4000 | all covered + masked transcript | anything re-identifying: exact dates, team size, project names, location | **high** |
| wykop | narrative | high | Title line + body (Mikroblog) | first person, plain Polish, no insults | body 600–4000 | as reddit | as reddit | **high** |
| blind | structured | moderate | Title / Tytuł; Pros / Plusy; Cons / Minusy (a review; one per former employer) | factual, first person | title ≤60; blocks 40–500 | all covered | team, level or office that identifies (the company is shown) | medium |
| x | short | low | one post, no thread | neutral | 80–270 (site 280) | 1–2 covered topics | any negative claim about the employer beyond the rating word | medium |

**Risk rule** (testable, spec §3.2): high if the identity is real-name or the detail is high; also high where pre-publication
moderation is absent and the poster's IP goes to law enforcement (GoWork); otherwise medium. Nothing is "low": every profile
publishes about a real employer.

**Warnings** (shown above each draft, EN; the Polish text is in the profile data). Each one is short, names the specific
risk, and ends "Draft, not fact. Not legal advice."

- glassdoor: "Glassdoor's terms make you the author of AI-assisted text. Name no one, share nothing confidential, and only review a job from the last 5 years."
- indeed: "One review per company, and it cannot be edited later. Indeed does not accept reports of crimes in reviews."
- gowork: "GoWork publishes without checking first and gives the poster's IP to law enforcement when an offence is suspected. Polish law protects personal rights; write opinions as opinions."
- linkedin: "This is posted under your real name, in front of future employers. Check any confidentiality or non-disparagement clause you signed."
- facebook_group: "Usually under your real name. Each group has its own rules; read them before posting."
- reddit: "The more detail, the easier you are to recognise. Each subreddit has its own rules, and some require AI-assisted text to be disclosed."
- wykop: "The more detail, the easier you are to recognise. Do not reveal anyone's personal data."
- blind: "Your company is shown with your post. A former employee may write one review per company."
- x: "Short public post. A screenshot outlives a deletion."
- kununu: not shown (disabled).

## 5. Findings about the current code (read, not run)

1. **The `google_review` tile conflicts with Google's policy** (section 3.11). It is retired in the spec (WP2, WP3).
2. **Wire mismatch.** The service maps every accepted kind to the wire ([`InterviewSessions.BuildTilesAsync`](../../src/ExitInterviewAgent.InterviewService/Interviews/InterviewSessions.cs)), including `what_worked`, `what_could_improve` and `for_the_next_person`.
   - The web parser ([`parseResult`](../../web/app/lib/interview-contract.ts)) accepts only six kinds and returns `null` for the whole result on any other.
   - The scripted mock emits all eight. The browser tests run against a stub backend, so they cannot catch this.
   - **Found by reading the code, not reproduced.** The spec fixes it in WP1.
3. Platform limits are hard-coded in `TileLimits` and in the writer prompt, and the web labels are hard-coded in `messages/interview.ts`. Adding a platform means editing four places.
4. The legal checklist's L2 ("do platform rules prohibit AI-written or service-drafted posts?") is partly answered by section 3.13. It stays a question for the lawyer.

## 6. What could not be verified

The full text of every official page; every length limit except LinkedIn's 3,000 and X's 280/25,000 (both S); all of Kununu;
the GoWork regulamin; Reddit's site rules page and every subreddit's rules; Blind's, Wykop's and Facebook's clauses on
automation and AI; any Polish Supreme Court ruling on GoWork. WP6 in the spec re-checks them from a normal network and
records the result in each profile's `sources`.

### Outcome of the WP6 re-check (session of 2026-10-12)

**Not done: the re-check could not reach the platforms.** The session that carried WP6 had the same egress limits as the
research session. Every platform host tried got no HTTP response. For glassdoor.com, DNS did not resolve and the egress proxy refused the
TLS tunnel with HTTP 403. The hosts tried were glassdoor.com, help.glassdoor.com, indeed.com, gowork.pl, kununu.com,
linkedin.com, reddit.com, help.teamblind.com, wykop.pl, help.x.com and transparency.meta.com. A control request to github.com
returned an HTTP response, so the failure is specific to these domains. The per-host cause was not recorded beyond that.

What this means, and what was changed as a result:

- **No URL in §3 was opened.** No fact was upgraded from S to `fetched`, and no number was corrected. Every S and UNVERIFIED
  marker stands as written, and no `sources[].level` was set, because the profile file is WP2's and is not on `main` yet.
- **Kununu stays disabled.** Its rules are still UNVERIFIED.
- **The `sources` update is still open.** It needs WP2's profile file on `main` and a session with normal web access.
- **Legal text is unchanged in substance.** Only the platform names in `terms.{en,pl}.md` §3 and the notes under checklist
  L2 changed, and both stay NOT REVIEWED. [OP-32](../OPEN-PROBLEMS.md) records the open problem.

To close this section, someone with normal web access opens each URL in §3, sets each fact's level, records the date, and
lists any number that differs.
