# 0005. Dependency update automation is declared and switched off

- Status: accepted
- Date: 2026-10-05
- Principle or guide served: **deviates from** `REPO-BASELINE.md` §1 (dependency update automation);
  `INIT-GENERIC-TEMPLATE.md` §4 prescribes the deviation and its reasoning.

## Context

A repository on its first day has no one to triage version bumps. Automation that opens pull requests
nobody owns produces a queue that is closed wholesale later, leaving worse hygiene than no automation
because everyone believes it is covered.

## Decision

`.github/dependabot.yml` declares every ecosystem the repository has (nuget, npm for `web/` and
`tests/e2e/`, github-actions, docker for both Dockerfiles), each with a weekly schedule and
`open-pull-requests-limit: 0`: declared, reviewed, opening no version-update pull requests.

What is covered anyway, from the first commit: `NuGetAudit=true`, `NuGetAuditMode=all`,
`NuGetAuditLevel=low` in `Directory.Build.props` fails the restore on a vulnerable package, transitive
ones included; the `dependency-audit` job in `.github/workflows/codeql.yml` runs `dotnet list package
--vulnerable --include-transitive` and `pnpm audit`. What is deferred is version *freshness*, not
vulnerability detection.

Dependabot **security updates and vulnerability alerts are repository settings, not this file**. They are
not changed by this decision. The setting was not readable from the authoring session, so its state is
unknown here: check Settings, Code security.

## Consequences

**Trigger for switching it on:** the repository gains a maintainer who triages bumps weekly, or it is made
public, whichever comes first (the owner's decision for the latter). Switching on is one line per
ecosystem: remove `open-pull-requests-limit: 0`. The deviation register in
`docs/architecture/00-ARCHITECTURE.md` carries the dated row.
