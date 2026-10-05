#!/usr/bin/env bash
# The regeneration rule (ADR-0040): when evals/baseline.json changes in a pull request, the PR description must carry a
# "Baseline justification: <text>" line equal to the justification stored in the file. Reads the description from $PR_BODY (an environment
# variable, never interpolated into the shell).
#   PR_BODY="$(cat description.md)" scripts/check-baseline-justification.sh origin/main
set -euo pipefail
base="${1:-origin/main}"
cd "$(git rev-parse --show-toplevel)"
if git diff --quiet "$base"...HEAD -- evals/baseline.json; then
  echo "evals/baseline.json is unchanged against $base: nothing to justify"
  exit 0
fi
stored="$(python3 -c "import json; print(json.load(open('evals/baseline.json'))['justification'].strip())")"
if [[ -z "$stored" ]]; then echo "evals/baseline.json has an empty justification"; exit 1; fi
line="$(printf '%s\n' "${PR_BODY:-}" | tr -d '\r' | grep -iE '^Baseline justification:' | head -n1 || true)"
if [[ -z "$line" ]]; then
  echo "evals/baseline.json changed but the pull request description has no line starting with 'Baseline justification:'"
  echo "Add: Baseline justification: $stored"
  exit 1
fi
text="$(printf '%s' "$line" | sed -E 's/^[Bb]aseline [Jj]ustification:[[:space:]]*//')"
if [[ "$text" != "$stored" ]]; then
  echo "the 'Baseline justification:' line does not equal the justification stored in evals/baseline.json"
  exit 1
fi
echo "baseline changed and justified: $stored"
