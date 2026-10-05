## What

<!-- One paragraph: what changes. -->

## Why

<!-- The reason, and the principle (P1-P15) or guide it serves or deviates from. -->

## Guides loaded

<!-- Standards guides read before writing the affected layer. -->

## ADRs added or changed

<!-- docs/adr/NNNN-*.md, or "none". -->

## How it was verified

<!-- Commands run and results. A gate you could not run is listed as NOT RUN. -->

- [ ] `dotnet build -warnaserror` and `dotnet test` green
- [ ] Frontend `pnpm lint`, `pnpm test`, `pnpm build` green (if `web/` touched)
- [ ] No secret, no real personal data, no model identifier in the diff
- [ ] README / docs claims still true of the tree

## Deferred

<!-- What this PR deliberately does not do. -->
