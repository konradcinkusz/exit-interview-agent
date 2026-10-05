#!/usr/bin/env bash
# Secret scan (P5, REPO-BASELINE §2). Runs gitleaks over the working tree, or over the staged
# changes only with --staged (used by the pre-commit hook). Uses a local `gitleaks` binary if
# present, otherwise the pinned container image. Fails closed: a scan that cannot run is a
# failure, never a silent pass.
set -euo pipefail

GITLEAKS_IMAGE="${GITLEAKS_IMAGE:-zricethezav/gitleaks:v8.28.0}"
repo_root="$(git rev-parse --show-toplevel)"
cd "$repo_root"

if [[ "${1:-}" == "--staged" ]]; then
  cmd=(git --staged)
else
  cmd=(dir)
fi
# gitleaks v8.28: `git --staged` scans staged changes, `dir` scans files without history.
sub="${cmd[0]}"; extra=("${cmd[@]:1}")

if command -v gitleaks >/dev/null 2>&1; then
  exec gitleaks "$sub" "${extra[@]}" --config .gitleaks.toml --redact --no-banner .
elif command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
  exec docker run --rm -v "$repo_root:/repo" -w /repo "$GITLEAKS_IMAGE" \
    "$sub" "${extra[@]}" --config /repo/.gitleaks.toml --redact --no-banner /repo
else
  echo "scan-secrets: neither 'gitleaks' nor a running Docker daemon is available." >&2
  echo "  Install gitleaks:  https://github.com/gitleaks/gitleaks#installing" >&2
  echo "  or start Docker. CI runs the same scan (.github/workflows/secret-scan.yml)." >&2
  exit 1
fi
