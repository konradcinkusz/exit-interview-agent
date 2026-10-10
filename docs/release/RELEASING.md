# Releasing the CLI

How to publish the `exit-interview` command-line tool as downloadable binaries, and how a user installs it. This is a runbook for the owner; running the workflow **is** the owner's decision to release (ADR-0073). Nothing here has been run on GitHub yet: the workflow passed `actionlint` and its publish and smoke steps were run locally for linux-x64 only.

## Cut a release

1. Read [`RELEASE-GATE.md`](RELEASE-GATE.md). The verdict is currently NOT READY; the release notes say so, so do not hide it.
2. GitHub → Actions → **Release CLI** → Run workflow on `main`.
   - `version`: no leading `v`, for example `0.1.0-pre.1`.
   - `publish`: leave off. The workflow then creates a **draft** release; no tag exists until you publish it.
   - `prerelease`: leave on while the gate is NOT READY.
3. The workflow builds `linux-x64`, `win-x64` and `osx-arm64` single-file self-contained binaries, smoke-tests the Linux one (`--version`, `personas`, a reproducible `demo`, `providers`), packages them (`.tar.gz` for Linux and macOS, `.zip` for Windows, each with `LICENSE`), writes `SHA256SUMS.txt` and creates the draft.
4. Open the draft under Releases, check the assets and the notes, and publish it. Publishing creates the tag `v<version>`.

If a release for the tag already exists the workflow stops. To redo a release, delete the draft (and the tag, if published) first.

## What is not done

- **No signing or notarisation.** There is no Windows code-signing certificate and no Apple Developer account in this project. Windows SmartScreen and macOS Gatekeeper will warn.
- **Windows and macOS binaries are built, not run.** CI executes only the Linux binary.
- **No package-manager channels** (winget, Homebrew, apt). Nothing in the repository sets these up.

## Installing (for users)

1. Download the archive for your system and `SHA256SUMS.txt` from the release page.
2. Verify: `sha256sum -c SHA256SUMS.txt --ignore-missing` (macOS: `shasum -a 256 -c SHA256SUMS.txt --ignore-missing`; Windows PowerShell: `Get-FileHash <file>` and compare by eye).
3. Unpack and put `exit-interview` somewhere on your `PATH`.
   - **macOS** (unsigned): `xattr -d com.apple.quarantine ./exit-interview`, or approve it in System Settings → Privacy & Security after the first blocked start.
   - **Windows** (unsigned): SmartScreen shows "Windows protected your PC"; More info → Run anyway. Only do this after step 2.
4. Try it offline: `exit-interview demo --persona talkative --seed 1` (sends nothing anywhere). See the guide chapters on the CLI for `interview`, providers and `submit`.

## Using it with your own apps

- **With a model of your choice:** `interview --provider anthropic` (your own API key), `--provider openai-compatible` or `--provider ollama` (local). A Claude or GitHub Copilot *subscription* cannot be used as the model backend; this is deliberate (README, "Supported model access").
- **With the Claude app:** that is the MCP mode, which needs a hosted server; see [`../guides/connect-claude.md`](../guides/connect-claude.md). It cannot talk to a server on your own machine unless that server is reachable from the internet over https.
