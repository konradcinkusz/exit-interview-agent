# Security policy

## Reporting a vulnerability

Please report suspected vulnerabilities privately through GitHub's
[private vulnerability reporting](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability)
for this repository, not in a public issue. If that is unavailable, contact the maintainer
through the address on their GitHub profile.

Include the affected component, a reproduction that uses no real personal data, and the
impact you expect. Expect an acknowledgement; there is no committed response time (this is a
single-maintainer project and no service is deployed).

## Scope and posture

- This project processes **simulated** interviews only; real personal data must never be
  committed, logged or used in a report. If you find any in the repository, report it as a
  vulnerability.
- Secrets are never committed. A secret scanner runs as a pre-commit hook and a CI job
  (`.github/workflows/secret-scan.yml`). If a secret reaches history, rotate it first.
- The privacy and threat model is tracked in `docs/` (see `docs/architecture/PROJECT-BRIEF.md`
  §6). Residual risks are listed there rather than hidden.
- Nothing is deployed. There is no production service to attack; findings against the code
  and the local development stack are in scope.
