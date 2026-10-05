#!/usr/bin/env bash
# One-command onboarding (REPO-BASELINE §3). Numbered steps; every optional step is labelled.
#   scripts/setup.sh           run all steps
#   scripts/setup.sh --check   report what is missing, change nothing
# Nothing here needs a cloud credential, and a fresh clone with every optional step skipped
# still runs (P8).
set -uo pipefail

check_only=false
[[ "${1:-}" == "--check" ]] && check_only=true
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
apphost="$repo_root/src/ExitInterviewAgent.AppHost"
missing=0

have() { command -v "$1" >/dev/null 2>&1; }
need() { # name, install pointer
  if have "$1"; then echo "  ok       $1"; else echo "  MISSING  $1 -> $2"; missing=$((missing + 1)); fi
}

echo "1/4 prerequisites"
need git "https://git-scm.com/downloads"
need dotnet ".NET SDK 10: https://dotnet.microsoft.com/download/dotnet/10.0"
need node "Node 22+: https://nodejs.org/en/download"
need pnpm "npm install -g pnpm   (or: corepack enable)"
if have docker && docker info >/dev/null 2>&1; then
  echo "  ok       container engine (docker)"
else
  echo "  MISSING  container engine -> https://docs.docker.com/get-docker/ (needed by the AppHost and image builds)"
  missing=$((missing + 1))
fi
if have dotnet; then
  sdk="$(dotnet --version 2>/dev/null || true)"
  [[ "$sdk" == 10.* ]] || { echo "  WRONG    dotnet $sdk found, global.json wants 10.x"; missing=$((missing + 1)); }
fi

if $check_only; then
  echo; echo "check: $missing item(s) missing"; exit $((missing > 0 ? 1 : 0))
fi
if (( missing > 0 )); then
  echo; echo "Install the missing prerequisites above and re-run. (--check lists them without changing anything.)"
  exit 1
fi

echo "2/4 git hooks (secret scan before every commit)"
git -C "$repo_root" config core.hooksPath scripts/hooks && echo "  core.hooksPath = scripts/hooks"

echo "3/4 local secret store (dotnet user-secrets, never files in the tree)"
dotnet user-secrets init --project "$apphost" >/dev/null 2>&1 || true
if dotnet user-secrets list --project "$apphost" 2>/dev/null | grep -q '^Parameters:authservice-jwt-private-key'; then
  echo "  authservice dev signing key already present"
elif have openssl; then
  pem="$(openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 2>/dev/null)"
  dotnet user-secrets set "Parameters:authservice-jwt-private-key" "$pem" --project "$apphost" >/dev/null \
    && echo "  generated a DEV-ONLY RSA-2048 signing key (PKCS#8) for the local authservice instance"
else
  echo "  openssl not found: skipping. The AppHost will generate an ephemeral dev key on each run."
fi

echo "4/4 optional integrations"
echo "  (optional - needed for a real model) none are wired yet: model providers arrive with the interview agent."
echo
echo "Done. Start the stack:   dotnet run --project src/ExitInterviewAgent.AppHost"
echo "Troubleshooting table:   scripts/README.md"
