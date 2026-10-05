#!/usr/bin/env bash
# P2's ceiling made mechanical: the shared kernel stays under ~800 lines of C#.
# Counts non-blank, non-comment lines of hand-written source (obj/ and bin/ excluded).
set -euo pipefail
LIMIT="${KERNEL_LINE_LIMIT:-800}"
dir="$(git rev-parse --show-toplevel)/src/ExitInterviewAgent.ServiceDefaults"
count=$(find "$dir" -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -print0 \
  | xargs -0 cat | grep -cvE '^\s*(//.*)?$' || true)
echo "ServiceDefaults: $count lines (limit $LIMIT)"
if (( count > LIMIT )); then
  echo "FAIL: the shared kernel exceeds $LIMIT lines. Domain code belongs in the service that owns it (P2)." >&2
  exit 1
fi
