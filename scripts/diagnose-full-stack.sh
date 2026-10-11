#!/usr/bin/env bash
# Live diagnostics for the full-stack job (ADR-0081). Run after the journeys: the test host has stopped its stack by then.
# It starts the AppHost by hand (the same arguments as the test host), probes the web-to-authservice hop from outside the
# test host, reads the web process's environment, then starts the web app by hand without Aspire and probes it too.
# Everything goes to one folder. Environment variables: only their names are written, except AUTH_*, the proxy variables,
# NODE_OPTIONS, NODE_DEBUG and PORT, whose values are the diagnostic. No request or response body is written except the
# consent versions (public) and the probe login, which uses an address that does not exist.
set -u
out="${1:?usage: scripts/diagnose-full-stack.sh <out-dir>}"
repo="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$out"
report="$out/report.txt"
say() { printf '%s\n' "$*" >> "$report"; }
section() { say ""; say "===== $* ====="; }

cd "$repo" || exit 1
export ASPNETCORE_ENVIRONMENT=Development Web__Script=start

section "Runner: Node, pnpm, dotnet, proxy variables (names only)"
say "node $(node -v 2>&1) at $(command -v node 2>&1)"
say "pnpm $(pnpm -v 2>&1)"
say "dotnet $(dotnet --version 2>&1)"
env | cut -d= -f1 | grep -i proxy >> "$report" 2>&1 || say "(no proxy variable in the runner environment)"

section "AppHost started by hand (dotnet run, arguments as in the test host)"
dotnet run --project src/ExitInterviewAgent.AppHost -c Release --no-launch-profile -- \
  --Parameters:interviews-provider=mock --Parameters:interviews-model=scripted \
  --Parameters:interviews-require-credit=true --Identity:Enabled=true > "$out/apphost.log" 2>&1 &
apphost=$!
say "AppHost pid $apphost"

wait_for() { # url, seconds
  for _ in $(seq 1 "$2"); do curl -fsS -m 3 -o /dev/null "$1" 2>/dev/null && return 0; sleep 5; done
  return 1
}
wait_for http://127.0.0.1:5100/health/ready 120 && say "authservice ready" || say "authservice not ready within 10 minutes"
wait_for http://127.0.0.1:3000/healthz 120 && say "web ready (Aspire)" || say "web not ready within 10 minutes (Aspire)"

section "Listeners (ss -ltnp)"
ss -ltnp >> "$report" 2>&1

section "Containers (docker ps -a)"
docker ps -a >> "$report" 2>&1

section "Authservice, direct (curl -sv, no body kept)"
for url in http://127.0.0.1:5100/health/ready http://localhost:5100/health/ready \
  http://127.0.0.1:5100/api/v1/auth/consents/versions http://localhost:5100/api/v1/auth/consents/versions; do
  say "-- $url"
  curl -sv -m 5 -o /dev/null "$url" >> "$report" 2>&1
done

section "Node fetch with the web app's Node, AbortSignal.timeout(5000)"
cat > "$out/probe.js" <<'EOF'
const urls = [
  "http://localhost:5100/api/v1/auth/consents/versions",
  "http://127.0.0.1:5100/api/v1/auth/consents/versions",
  "http://[::1]:5100/api/v1/auth/consents/versions",
];
(async () => {
  const dns = require("node:dns");
  dns.lookup("localhost", { all: true }, (err, addresses) =>
    console.log("localhost ->", err ? err.code : JSON.stringify(addresses)));
  for (const url of urls) {
    const started = Date.now();
    try {
      const response = await fetch(url, { redirect: "manual", signal: AbortSignal.timeout(5000) });
      await response.text();
      console.log(url, "status", response.status, Date.now() - started, "ms");
    } catch (e) {
      console.log(url, "error", e.name, "cause", e.cause?.code ?? "none", Date.now() - started, "ms");
    }
  }
})();
EOF
node "$out/probe.js" >> "$report" 2>&1

section "BFF on the Aspire web app (port 3000)"
say "-- GET /api/auth/consent-versions"
curl -sv -m 20 http://127.0.0.1:3000/api/auth/consent-versions >> "$report" 2>&1
say ""
say "-- POST /api/auth/login (a probe address that does not exist, same Origin as the journeys)"
curl -sv -m 20 -X POST -H 'content-type: application/json' -H 'Origin: http://localhost:3000' \
  -d '{"email":"probe@example.invalid","password":"probe"}' http://127.0.0.1:3000/api/auth/login >> "$report" 2>&1

section "Web process environment (names; values for AUTH_*, proxy, NODE_*, PORT)"
web_pid="$(ss -ltnpH 'sport = :3000' 2>/dev/null | grep -o 'pid=[0-9]*' | head -1 | cut -d= -f2)"
say "web listener pid: ${web_pid:-not found}"
if [ -n "${web_pid:-}" ] && [ -r "/proc/$web_pid/environ" ]; then
  tr '\0' '\n' < "/proc/$web_pid/environ" | while IFS= read -r line; do
    case "${line%%=*}" in
      AUTH_* | HTTP_PROXY | HTTPS_PROXY | NO_PROXY | http_proxy | https_proxy | no_proxy | NODE_OPTIONS | NODE_DEBUG | PORT) say "$line" ;;
      *) say "${line%%=*} (value not shown)" ;;
    esac
  done
fi

section "authservice container logs (tail 200)"
mkdir -p "$out/containers"
docker ps -a --format '{{.Names}}' | grep -i authservice | while read -r name; do
  docker logs --tail 200 "$name" > "$out/containers/$name.log" 2>&1
  say "-- $name: $out/containers/$name.log"
done

section "Web app started by hand (pnpm start, no Aspire), port 3100"
(
  cd "$repo/web/app" || exit 1
  PORT=3100 AUTH_SERVICE_URL=http://localhost:5100 AUTH_ISSUER=ExitInterviewAgent AUTH_AUDIENCE=ExitInterviewAgent \
    SESSION_COOKIE_SECURE=false NODE_DEBUG=net pnpm start > "$out/web-manual.log" 2>&1
) &
manual=$!
wait_for http://127.0.0.1:3100/healthz 60 && say "manual web ready" || say "manual web not ready within 5 minutes"
say "-- GET /api/auth/consent-versions (manual web)"
curl -sv -m 20 http://127.0.0.1:3100/api/auth/consent-versions >> "$report" 2>&1
say ""
say "-- POST /api/auth/login (manual web)"
curl -sv -m 20 -X POST -H 'content-type: application/json' -H 'Origin: http://localhost:3100' \
  -d '{"email":"probe@example.invalid","password":"probe"}' http://127.0.0.1:3100/api/auth/login >> "$report" 2>&1

section "Cleanup"
kill "$manual" 2>/dev/null
kill -INT "$apphost" 2>/dev/null
for _ in $(seq 1 24); do kill -0 "$apphost" 2>/dev/null || break; sleep 5; done
say "AppHost stopped"
