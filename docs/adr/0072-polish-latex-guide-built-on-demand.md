# ADR-0072: Polish LaTeX guide to the product, built on demand

- Status: accepted
- Date: 2026-10-10
- Principle or guide served (or deviated from): P14 (documentation lives in the repository and records its reasoning), ADR-0017 (documentation layout and claim status), `architecture-standards` `docs/research/00-RESEARCH-DOCUMENTATION.md` (house LaTeX preamble; "PDFs are build output")

## Context

The product documentation is complete but spread across many files under `docs/`: ADRs, architecture notes, the evaluation specification and results, privacy and security notes, and open problems. That shape suits a contributor with a clone. It does not suit a reader who wants the whole product in one sitting, in Polish, without navigating the repository.

`architecture-standards` prescribes a house LaTeX preamble (`docs/papers/house-preamble.tex`) so that documents from this estate look alike. The standard's LaTeX guidance is written for a paper derived from a research study. The guide here answers no new research question; it presents what `docs/` already records. Filing it as a study would misuse the term the standard defines narrowly.

The guide already exists in the repository (`docs/papers/przewodnik.pl.tex`, merged in #19). Its header states the build commands and that the PDF is build output. The chapters are split into `czesc-1-produkt.tex` to `czesc-4-jakosc.tex` and pulled in with `\input`, so the document runs to dozens of pages but still has one build entry point. Diagrams are rendered by `scripts/render-diagrams.sh` before the LaTeX build. The build runs in `.github/workflows/build-guide-pdf.yml`, which is `workflow_dispatch` only, uploads the PDF as a run artifact with 30-day retention, and is not committed.

Facts checked for this record:

- The repository holds 64 ADR files plus the template (`ls docs/adr/[0-9]*.md | wc -l` gives 65), not the 71 the index used to claim.
- The guide's own text (`czesc-4-jakosc.tex`) reports the same 64 files and the numbering gaps 0015–0016, 0020–0021 and 0064–0066.
- `docs/papers/` has no committed PDF, and the workflow's only trigger is `workflow_dispatch`.
- The workflow has not yet run on GitHub. Nothing has been built there.

## Decision

Keep the Polish guide as LaTeX source in `docs/papers/`, split into `przewodnik.pl.tex` and the four `czesc-*.tex` chapters, and reuse the house preamble unchanged. Build it on demand, in two ways:

- locally, with the commands in the header of `przewodnik.pl.tex`;
- in CI, through `build-guide-pdf.yml` on `workflow_dispatch` only. The job renders the diagrams, runs `xu-cheng/latex-action`, and uploads `ExitInterviewAgent_Przewodnik_PL.pdf` as an artifact.

Do not commit the PDF, and do not trigger the build on push, pull request or tag.

## Alternatives considered

### Pandoc from the Markdown in `docs/`, with no LaTeX source

**Why it is attractive:** no second copy of the content to maintain, and no hand transcription into LaTeX.

**Why it lost:** the estate already has a house look for formal documents. Pandoc's default template does not produce it without a custom template that would need to match the preamble anyway, so reusing the existing preamble is less work.

### Commit a pre-built PDF

**Why it is attractive:** works without CI and without a TeX installation.

**Why it lost:** it violates "PDFs are build output". A committed PDF goes stale the first time a file under `docs/` changes and no one rebuilds it.

### Build on tag (`v*`)

**Why it is attractive:** the PDF would ship with each release.

**Why it lost:** this documentation has no release cadence of its own. A `v*` tag is reserved for product release, and the repository's rules forbid pushing those tags for now. A tag-driven build would also publish a PDF built against whatever the last tag contained.

### An English edition alongside the Polish one

**Why it is attractive:** reaches readers who do not read Polish.

**Why it lost (for now):** it doubles the content that must be kept in step with `docs/`, which is already an accepted drift risk (see Consequences). The Polish edition covers the current audience. An English edition is a separate decision that can be taken once there is a reader for it.

## Consequences

**What this makes easy:**

- A reader gets the whole product in one document, without a clone.
- Regenerating the PDF takes one click on **Run workflow**, with no local LaTeX install.
- The visual result matches the author's other formal documents, with no new template.

**What this makes harder:**

- The `.tex` chapters and `docs/` can drift. The chapters are a curated presentation, not a mechanical transform, and nothing forces an edit under `docs/` to reach the guide. This is accepted. The guide's header says so and does not claim to be current to the last commit.
- The `.tex` chapters repeat figures from `docs/research/RESULTS.md`, so a change to those results needs a manual update to the guide. Figures in the guide must stay reproducible from the repository.

**What we accept:**

- `build-guide-pdf.yml` has not yet run on GitHub. Its first run will be the first test. A break in `xu-cheng/latex-action` or in the TeX toolchain shows up only when someone starts the workflow by hand, not on every push.
- The PDF is available only as a run artifact that expires after 30 days, so a reader has to ask for a fresh build after that.

## Revisit when

- The guide visibly diverges from `docs/` more than once. Add a lightweight check (for example, a section-heading comparison) rather than a full change-coupling rule.
- The workflow fails on its first run, or breaks after an upstream change. Fix it, and record the failure mode here.
- There is a reader for an English edition. Take that as a separate decision.
- The repository gains a release cadence for its documentation. Reconsider the tag-driven alternative at that point.
