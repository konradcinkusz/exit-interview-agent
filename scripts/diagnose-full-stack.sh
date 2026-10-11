#!/usr/bin/env bash
# Live diagnostics for the full-stack job (ADR-0081). Run after the journeys: the test host has stopped its stack by then.
# It starts the AppHost by hand (the same arguments as the test host), probes the web-to-authservice hop from outside the
# test host, reads the web process's environment, then starts the web app by hand without Aspire and probes it too.
# Everything goes to one folder. Every external call has a timeout, every background process has its own stdin, stdout and
# stderr (files), and every wait ends when its process has died. Environment variables: only their names are written, except
# AUTH_*, the proxy variables, NODE_OPTIONS, NODE_DEBUG and PORT, whose values are the diagnostic.
set -u
out="${1:?usage: scripts/diagnose-full-stack.sh <out-dir>}"
repo="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$out"
report="$out/report.txt"
say() { printf '%s\n' "$*" >> "$report"; }
section() { say ""; say "===== $* ====="; }
export ASPNETCORE_ENVIRONMENT=Development Web__Script=start

cd "$repo" || exit 1

section "Runner: Node, pnpm, dotnet, proxy variables (names only)"
say "node $(timeout 10 node -v 2>&1) at $(command -v node 2>&1)"
say "pnpm $(timeout 10 pnpm -v 2>&1)"
say "dotnet $(timeout 20 dotnet --version 2>&1)"
env | cut -d= -f1 | grep -i proxy >> "$report" 2>&1 || say "(no proxy variable in the runner environment)"

section "State left by the journeys (listeners, containers)"
timeout 10 ss -ltnp >> "$report" 2>&1
timeout 20 docker ps -a >> "$report" 2>&1
# The test host stops its containers on dispose; any left over would hold the fixed ports. Removed here, and recorded.
timeout 60 docker ps -aq --filter name=authservice --filter name=postgres --filter name=pgadmin | xargs -r timeout 60 docker rm -f >> "$report" 2>&1
say "after removing leftovers:"
timeout 20 docker ps -a >> "$report" 2>&1

wait_for() { # url, seconds, pid of the process that must stay alive, name
  local elapsed=0
  while [ "$elapsed" -lt "$2" ]; do
    if timeout 5 curl -fsS -m 3 -o /dev/null "$1" 2>/dev/null; then say "$4 ready after ${elapsed}s"; return 0; fi
    if [ -n "${3:-}" ] && ! kill -0 "$3" 2>/dev/null; then say "$4: its process ($3) exited after ${elapsed}s"; return 1; fi
    sleep 5
    elapsed=$((elapsed + 5))
  done
  say "$4 not ready after ${2}s"
  return 1
}

section "AppHost started by hand (dotnet run, arguments as in the test host)"
nohup dotnet run --project src/ExitInterviewAgent.AppHost -c Release --no-launch-profile -- \
  --Parameters:interviews-provider=mock --Parameters:interviews-model=scripted \
  --Parameters:interviews-require-credit=true --Identity:Enabled=true \
  < /dev/null > "$out/apphost.log" 2>&1 &
apphost=$!
say "AppHost pid $apphost"
if wait_for http://127.0.0.1:5100/health/ready 240 "$apphost" "authservice (AppHost)"; then
  wait_for http://127.0.0.1:3000/healthz 240 "$apphost" "web (AppHost)" || true
fi
say "--- AppHost log (last 80 lines)"
tail -n 80 "$out/apphost.log" >> "$report" 2>&1

section "Listeners and containers with the AppHost running"
timeout 10 ss -ltnp >> "$report" 2>&1
timeout 20 docker ps -a >> "$report" 2>&1

section "Authservice, direct (curl -sv, body discarded)"
for url in http://127.0.0.1:5100/health/ready http://localhost:5100/health/ready \
  http://127.0.0.1:5100/api/v1/auth/consents/versions http://localhost:5100/api/v1/auth/consents/versions; do
  say "-- $url"
  timeout 15 curl -sv -m 5 -o /dev/null "$url" >> "$report" 2>&1
done
say "-- full answer headers, as the web app asks for them (curl -si, body first 200 bytes)"
timeout 15 curl -si -m 5 -H 'Accept: application/json' http://127.0.0.1:5100/api/v1/auth/consents/versions 2>&1 | head -c 1500 >> "$report"
say ""

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
      console.log(url, "status", response.status, "ok", response.ok, "location", response.headers.get("location"), Date.now() - started, "ms");
    } catch (e) {
      console.log(url, "error", e.name, "cause", e.cause?.code ?? "none", Date.now() - started, "ms");
    }
  }
})();
EOF
timeout 60 node "$out/probe.js" >> "$report" 2>&1

section "BFF on the Aspire web app (port 3000)"
say "-- GET /api/auth/consent-versions"
timeout 30 curl -sv -m 20 http://127.0.0.1:3000/api/auth/consent-versions >> "$report" 2>&1
say ""
say "-- POST /api/auth/login (a probe address that does not exist, same Origin as the journeys)"
timeout 30 curl -sv -m 20 -X POST -H 'content-type: application/json' -H 'Origin: http://localhost:3000' \
  -d '{"email":"probe@example.invalid","password":"probe"}' http://127.0.0.1:3000/api/auth/login >> "$report" 2>&1

section "Web process environment (names; values for AUTH_*, proxy, NODE_*, PORT)"
web_pid="$(timeout 10 ss -ltnpH 'sport = :3000' 2>/dev/null | grep -o 'pid=[0-9]*' | head -1 | cut -d= -f2)"
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
timeout 20 docker ps -a --format '{{.Names}}' | grep -i authservice | while read -r name; do
  timeout 30 docker logs --tail 200 "$name" > "$out/containers/$name.log" 2>&1
  say "-- $name: $out/containers/$name.log"
done

section "Web app started by hand (pnpm start, no Aspire), port 3100"
(
  cd "$repo/web/app" || exit 1
  PORT=3100 AUTH_SERVICE_URL=http://localhost:5100 AUTH_ISSUER=ExitInterviewAgent AUTH_AUDIENCE=ExitInterviewAgent \
    SESSION_COOKIE_SECURE=false NODE_DEBUG=net exec pnpm start < /dev/null > "$out/web-manual.log" 2>&1
) &
manual=$!
if wait_for http://127.0.0.1:3100/healthz 180 "$manual" "manual web"; then
  say "-- GET /api/auth/consent-versions (manual web)"
  timeout 30 curl -sv -m 20 http://127.0.0.1:3100/api/auth/consent-versions >> "$report" 2>&1
  say ""
  say "-- POST /api/auth/login (manual web)"
  timeout 30 curl -sv -m 20 -X POST -H 'content-type: application/json' -H 'Origin: http://localhost:3100' \
    -d '{"email":"probe@example.invalid","password":"probe"}' http://127.0.0.1:3100/api/auth/login >> "$report" 2>&1
fi

section "Cleanup"
kill "$manual" 2>/dev/null
kill -INT "$apphost" 2>/dev/null
for _ in $(seq 1 24); do kill -0 "$apphost" 2>/dev/null || break; sleep 5; done
say "AppHost stopped (or still running after 2 minutes; the job ends regardless)"

# The job log carries the evidence too (the artifact may not be readable from every place): the report, then the tails.
echo "===== report.txt ====="
cat "$report"
echo "===== apphost.log (last 150 lines; the resource logs of the AppHost run) ====="
tail -n 150 "$out/apphost.log"
echo "===== web-manual.log (last 100 lines) ====="
tail -n 100 "$out/web-manual.log" 2>/dev/null
exit 0
