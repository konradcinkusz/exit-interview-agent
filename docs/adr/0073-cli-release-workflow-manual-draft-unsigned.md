# ADR-0073: CLI release workflow is manual, draft by default, and unsigned

- Status: accepted
- Date: 2026-10-10
- Principle or guide served (or deviated from): brief §2 (no deployment, no releases, no tags before the owner decides: this workflow is how the owner decides), ADR-0026 (CLI binaries as workflow artifacts), `repo-baseline` (CI proves what the README claims)

## Context

ADR-0026 builds self-contained single-file CLI binaries for linux-x64, win-x64 and osx-arm64 and keeps them as workflow artifacts for 7 days. Artifacts need a GitHub login and expire, so they are not a way to give the tool to other people. The owner asked to be able to share the CLI for local, offline installation.

Facts that shape the decision: there is no code-signing certificate and no Apple Developer account in this project; only the Linux binary can run on the CI runner; the release gate currently reads NOT READY (`docs/release/RELEASE-GATE.md`); the brief forbids releases and tags until the owner decides.

## Decision

Add `.github/workflows/release-cli.yml`:

- `workflow_dispatch` only. Inputs: `version`, `publish` (default off), `prerelease` (default on). The version is validated against a SemVer pattern and stamped into the assemblies.
- By default it creates a **draft** GitHub Release. A draft does not create the tag; the tag `v<version>` appears when the owner publishes it. Passing `publish` creates the release and tag at once.
- It builds the three binaries as ADR-0026 does, smoke-tests the Linux one, packages each with `LICENSE`, and adds `SHA256SUMS.txt`.
- Permissions are `contents: read` for the build matrix and `contents: write` only for the job that creates the release. Inputs reach shell steps through `env`, never interpolated into the script.
- The release notes state that the binaries are unsigned, that Windows and macOS were not run, and that the gate is NOT READY.

## Alternatives considered

- **Tag-triggered release** (as `flyio.yml`): rejected; the brief forbids tags before the owner decides, and a push would then release without an explicit decision.
- **Publish immediately by default:** rejected; a draft costs one click and allows checking the assets first.
- **Sign and notarise now:** not possible without a certificate and an Apple account the project does not have. Recorded as an owner follow-up; the notes and `RELEASING.md` tell users what the warnings mean.
- **Package-manager channels (winget, Homebrew):** deferred; they would need signing and a release cadence first.

## Consequences

- The owner can share installable binaries by running one workflow and clicking publish.
- Users on Windows and macOS see security warnings and run binaries nobody has executed in CI. This is stated, not hidden.
- The workflow has not run on GitHub when this ADR is written; it passed `actionlint`, and its publish and smoke steps were run locally for linux-x64 only. Its first real run is its first full test.
- `--version` prints the interview protocol version, not the release version, so the smoke test cannot check the stamped version. Not changed here.
