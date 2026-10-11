#!/usr/bin/env bash
# Smoke check of the local stack started by the AppHost (docs/runbooks/local-run.md).
#   scripts/smoke-local.sh --interview-url http://localhost:<port> [--token <access token>]
#   scripts/smoke-local.sh --interview-url <url> --dry-run     no network: prints the checks, validates the arguments
#
# Checks, each printed as PASS or FAIL:
#   interview-service  GET <interview-url>/health           answers 200 and says what is degraded (WARN, not FAIL)
#   web                GET <web-url>/healthz                 answers 200
#   authservice        GET <auth-url>/health/ready           answers 200
# With --token, one scripted interview goes through the API: start, reply until the end, read the result, delete.
# Nothing is written to a file. The token is sent only to the interview-service, never printed.
#
# Ports: web and authservice have fixed ports (3000, 5100). interview-service's port is assigned by Aspire, so it is
# a required argument: read it from the dashboard (the interview-service endpoint).
# Exit status: 0 when every check passed (WARN lines allowed), 1 when a check failed, 2 on bad usage.
set -uo pipefail

web_url="http://localhost:3000"
auth_url="http://localhost:5100"
interview_url=""
token=""
timeout=10
dry_run=false

usage() {
  sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --web-url) web_url="${2:-}"; shift 2 ;;
    --auth-url) auth_url="${2:-}"; shift 2 ;;
    --interview-url) interview_url="${2:-}"; shift 2 ;;
    --token) token="${2:-}"; shift 2 ;;
    --timeout) timeout="${2:-10}"; shift 2 ;;
    --dry-run) dry_run=true; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done

if [[ -z "$interview_url" ]]; then
  echo "FAIL usage: --interview-url is required (the interview-service port is assigned by Aspire; see the dashboard)" >&2
  exit 2
fi
for u in "$web_url" "$auth_url" "$interview_url"; do
  [[ "$u" =~ ^https?://[^[:space:]]+$ ]] || { echo "FAIL usage: not an http(s) URL: $u" >&2; exit 2; }
done
[[ "$timeout" =~ ^[0-9]+$ ]] || { echo "FAIL usage: --timeout must be a whole number of seconds" >&2; exit 2; }
command -v python3 >/dev/null 2>&1 || { echo "FAIL prerequisite: python3 is needed to read JSON answers" >&2; exit 2; }

failures=0
pass() { echo "PASS  $1"; }
warn() { echo "WARN  $1"; }
fail() { echo "FAIL  $1"; failures=$((failures + 1)); }

# json_field <key>: prints one top-level field of the JSON on stdin ("" when absent or null).
json_field() {
  python3 -c 'import json,sys
try:
    v = json.load(sys.stdin).get(sys.argv[1])
except Exception:
    v = None
print("" if v is None else v)' "$1"
}

# http <method> <url> [json body]: prints the status code on the first line, the body after it. Token is passed in a header only.
http() {
  local method="$1" url="$2" body="${3:-}"
  local args=(-sS -m "$timeout" -X "$method" -w '\n%{http_code}' -H 'accept: application/json')
  [[ -n "$token" ]] && args+=(-H "authorization: Bearer $token")
  [[ -n "$body" ]] && args+=(-H 'content-type: application/json' --data "$body")
  curl "${args[@]}" "$url" 2>/dev/null
}

if $dry_run; then
  echo "dry run: no request is sent"
  echo "would check interview-service  GET $interview_url/health"
  echo "would check web                GET $web_url/healthz"
  echo "would check authservice        GET $auth_url/health/ready"
  if [[ -n "$token" ]]; then
    echo "would run one scripted interview: POST $interview_url/api/v1/interviews, reply until the end, GET result, DELETE"
  else
    echo "interview flow skipped: pass --token to run it"
  fi
  echo "arguments are valid"
  exit 0
fi

# 1. interview-service: the health answer lists each integration with its state.
out="$(http GET "$interview_url/health")"; code="${out##*$'\n'}"; body="${out%$'\n'*}"
if [[ "$code" == "200" ]]; then
  pass "interview-service /health answered 200"
  python3 -c 'import json,sys
d = json.load(sys.stdin)
print("      overall: " + str(d.get("status")))
for i in d.get("integrations", []):
    state = "configured" if i.get("configured") else "DEGRADED"
    print("      " + str(i.get("name")) + ": " + state + " (" + str(i.get("detail")) + ")")' <<<"$body" 2>/dev/null || warn "interview-service /health body is not JSON"
  if python3 -c 'import json,sys; d=json.load(sys.stdin); sys.exit(0 if any(i.get("name")=="interview-model" and not i.get("configured") for i in d.get("integrations",[])) else 1)' <<<"$body" 2>/dev/null; then
    warn "interview-model is DEGRADED: the scripted mock answers, not a real model (expected without anthropic-api-key)"
  fi
else
  fail "interview-service /health answered ${code:-no answer} (000 = no connection; is the AppHost running, and is --interview-url its port?)"
fi

# 2. web: a dedicated probe, not /, which would pass on a broken bundle.
out="$(http GET "$web_url/healthz")"; code="${out##*$'\n'}"; body="${out%$'\n'*}"
if [[ "$code" == "200" && "$(json_field status <<<"$body")" == "ok" ]]; then
  pass "web /healthz answered 200 with status ok"
else
  fail "web /healthz answered ${code:-no answer} (000 = no connection; is the web app up on $web_url?)"
fi

# 3. authservice: readiness (database migrated, signing key loaded).
out="$(http GET "$auth_url/health/ready")"; code="${out##*$'\n'}"
if [[ "$code" == "200" ]]; then
  pass "authservice /health/ready answered 200"
else
  fail "authservice /health/ready answered ${code:-no answer} (000 = no connection; image pulled? container running? see the runbook's troubleshooting)"
fi

# 4. optional: one scripted interview through the API. It needs a token from a login (the runbook says how).
if [[ -z "$token" ]]; then
  echo "SKIP  interview flow: pass --token to run it"
else
  out="$(http POST "$interview_url/api/v1/interviews" '{"language":"en","tenure":"1y_3y"}')"; code="${out##*$'\n'}"; body="${out%$'\n'*}"
  id="$(json_field id <<<"$body")"
  if [[ "$code" == "201" && -n "$id" ]]; then
    pass "start an interview: 201"
    reply_text="The work was fine, but the goals kept changing without notice."
    ended=""
    for _ in $(seq 1 40); do
      out="$(http POST "$interview_url/api/v1/interviews/$id/reply" "{\"text\":\"$reply_text\"}")"; code="${out##*$'\n'}"; body="${out%$'\n'*}"
      if [[ "$code" == "409" ]]; then sleep 1; continue; fi   # reply_in_progress: the previous turn is still being produced
      [[ "$code" == "200" ]] || break
      if [[ -n "$(python3 -c 'import json,sys; e=json.load(sys.stdin).get("ending"); print("yes" if e else "")' <<<"$body" 2>/dev/null)" ]]; then ended="yes"; break; fi
    done
    if [[ "$ended" == "yes" ]]; then pass "reply until the interview ends: ended"; else fail "reply until the interview ends: last answer ${code:-none}, no ending within 40 replies"; fi

    result_code=""
    for _ in $(seq 1 30); do
      out="$(http GET "$interview_url/api/v1/interviews/$id/result")"; result_code="${out##*$'\n'}"
      [[ "$result_code" == "409" ]] || break   # not_completed: the tiles are still being made
      sleep 1
    done
    if [[ "$result_code" == "200" ]]; then pass "read the result: 200"; else fail "read the result: ${result_code:-no answer}"; fi

    out="$(http DELETE "$interview_url/api/v1/interviews/$id")"; code="${out##*$'\n'}"
    if [[ "$code" == "204" ]]; then pass "delete the interview: 204"; else fail "delete the interview: ${code:-no answer}"; fi
  else
    fail "start an interview: ${code:-no answer} (401: token wrong or expired; 402: RequireCredit is on and no credit; 429: 5 starts per hour; 503: interviews disabled)"
  fi
fi

echo
if (( failures == 0 )); then
  echo "smoke: all checks passed"
  exit 0
fi
echo "smoke: $failures check(s) failed"
exit 1
