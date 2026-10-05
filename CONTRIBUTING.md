# Contributing

Read [`docs/architecture/PROJECT-BRIEF.md`](docs/architecture/PROJECT-BRIEF.md) first. It is
binding: the goal, the non-goals (no deployment, no public repository, no real personal
data, no subscription-token or Copilot model backend), the decisions already taken, and the
operating contract for every change.

## The standards

The repository is built to
[`konradcinkusz/architecture-standards`](https://github.com/konradcinkusz/architecture-standards).
Read the standard rather than re-deriving it, and load the guide for the layer you touch
before writing it (`docs/architecture/00-ARCHITECTURE.md` lists them). A deviation from a
principle needs an ADR under [`docs/adr/`](docs/adr/) citing the principle, and a dated row
in the deviation register in `docs/architecture/00-ARCHITECTURE.md`.

## Workflow

1. Branch off the latest `main`: one task, one branch `claude/<task-slug>` (or your own
   prefix), one pull request.
2. Run `scripts/setup.sh` (or `scripts/setup.ps1`) once: it checks prerequisites, installs
   the pre-commit secret scan and initialises the local secret store.
3. Prove the change before pushing: `dotnet build -warnaserror`, `dotnet test`, and in
   `web/` `pnpm lint && pnpm test && pnpm build`. List in the PR what you ran and what you
   could not run; never claim a gate you did not run.
4. Fill in the pull request template. CI must be green; never skip, disable or quarantine a
   test to get there.
5. Squash-merge, then delete the branch.

## Rules that are easy to break

- Never commit a secret or real personal data. Development secrets are generated, not
  invented, and are documented as dev-only.
- The shared kernel (`ExitInterviewAgent.ServiceDefaults`) holds plumbing only: no entity,
  DTO, enum, seed data or user-facing string. CI enforces a size ceiling and an architecture
  test.
- Do not copy code from `authservice`; it is consumed as a pinned image.
- Documentation is English. Every number in a document comes from a command that reproduces
  it.

## Licence

By contributing you agree your contribution is licensed under the [MIT licence](LICENSE).
