#!/usr/bin/env bash
# Renders every docs/diagrams/*.mmd to vector PDF in docs/diagrams/rendered/.
# Pinned renderer: docs/papers/package.json (run `npm ci` there first).
# Set CHROME_BIN / PUPPETEER_EXECUTABLE_PATH to reuse an installed Chromium.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
mmdc="$root/docs/papers/node_modules/.bin/mmdc"
[ -x "$mmdc" ] || { echo "mmdc missing: run 'npm ci' in docs/papers" >&2; exit 1; }
cfg="$(mktemp)"; trap 'rm -f "$cfg"' EXIT
chrome="${PUPPETEER_EXECUTABLE_PATH:-${CHROME_BIN:-}}"
if [ -n "$chrome" ]; then
  printf '{"executablePath":"%s","args":["--no-sandbox","--disable-setuid-sandbox","--disable-dev-shm-usage"]}' "$chrome" > "$cfg"
else
  printf '{"args":["--no-sandbox","--disable-setuid-sandbox","--disable-dev-shm-usage"]}' > "$cfg"
fi
mkdir -p "$root/docs/diagrams/rendered"
for f in "$root"/docs/diagrams/*.mmd; do
  [ $# -eq 0 ] || [[ " $* " == *" $(basename "$f" .mmd) "* ]] || continue
  "$mmdc" -i "$f" -o "$root/docs/diagrams/rendered/$(basename "$f" .mmd).pdf" --pdfFit -b transparent -p "$cfg"
done
