# Smoke check of the local stack started by the AppHost (docs/runbooks/local-run.md). Mirrors scripts/smoke-local.sh.
#   scripts/smoke-local.ps1 -InterviewUrl http://localhost:<port> [-Token <access token>]
#   scripts/smoke-local.ps1 -InterviewUrl <url> -DryRun      no network: prints the checks, validates the arguments
#
# Checks, each printed as PASS or FAIL:
#   interview-service  GET <InterviewUrl>/health        answers 200 and says what is degraded (WARN, not FAIL)
#   web                GET <WebUrl>/healthz              answers 200
#   authservice        GET <AuthUrl>/health/ready        answers 200
# With -Token, one scripted interview goes through the API: start, reply until the end, read the result, delete.
# Nothing is written to a file. The token is sent only to the interview-service, never printed.
#
# Ports: web and authservice have fixed ports (3000, 5100). interview-service's port is assigned by Aspire, so it is
# a required parameter: read it from the dashboard (the interview-service endpoint).
# Exit status: 0 when every check passed (WARN lines allowed), 1 when a check failed, 2 on bad usage.
param(
    [string]$WebUrl = 'http://localhost:3000',
    [string]$AuthUrl = 'http://localhost:5100',
    [string]$InterviewUrl,
    [string]$Token,
    [int]$TimeoutSec = 10,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$failures = 0

function Pass($text) { Write-Host "PASS  $text" -ForegroundColor Green }
function Warn($text) { Write-Host "WARN  $text" -ForegroundColor Yellow }
function Fail($text) { Write-Host "FAIL  $text" -ForegroundColor Red; $script:failures++ }

if ([string]::IsNullOrWhiteSpace($InterviewUrl)) {
    Write-Host 'FAIL usage: -InterviewUrl is required (the interview-service port is assigned by Aspire; see the dashboard)'
    exit 2
}
foreach ($u in @($WebUrl, $AuthUrl, $InterviewUrl)) {
    if ($u -notmatch '^https?://\S+$') { Write-Host "FAIL usage: not an http(s) URL: $u"; exit 2 }
}

if ($DryRun) {
    Write-Host 'dry run: no request is sent'
    Write-Host "would check interview-service  GET $InterviewUrl/health"
    Write-Host "would check web                GET $WebUrl/healthz"
    Write-Host "would check authservice        GET $AuthUrl/health/ready"
    if ($Token) { Write-Host "would run one scripted interview: POST $InterviewUrl/api/v1/interviews, reply until the end, GET result, DELETE" }
    else { Write-Host 'interview flow skipped: pass -Token to run it' }
    Write-Host 'arguments are valid'
    exit 0
}

# Invoke-Api: returns @{ Code = <int or 0 when no connection>; Body = <string> }. Works on Windows PowerShell 5.1 and PowerShell 7.
function Invoke-Api([string]$Method, [string]$Url, [string]$Json) {
    $headers = @{ accept = 'application/json' }
    if ($Token) { $headers['authorization'] = "Bearer $Token" }
    $params = @{ Uri = $Url; Method = $Method; Headers = $headers; TimeoutSec = $TimeoutSec; UseBasicParsing = $true }
    if ($Json) { $params['Body'] = $Json; $params['ContentType'] = 'application/json' }
    try {
        $r = Invoke-WebRequest @params
        return @{ Code = [int]$r.StatusCode; Body = [string]$r.Content }
    } catch {
        $resp = $_.Exception.Response
        if ($null -eq $resp) { return @{ Code = 0; Body = '' } }
        $body = ''
        try { $body = (New-Object System.IO.StreamReader($resp.GetResponseStream())).ReadToEnd() } catch { }
        return @{ Code = [int]$resp.StatusCode.value__; Body = $body }
    }
}

# 1. interview-service: the health answer lists each integration with its state.
$r = Invoke-Api 'GET' "$InterviewUrl/health"
if ($r.Code -eq 200) {
    Pass 'interview-service /health answered 200'
    $h = $r.Body | ConvertFrom-Json
    Write-Host "      overall: $($h.status)"
    foreach ($i in $h.integrations) {
        $state = if ($i.configured) { 'configured' } else { 'DEGRADED' }
        Write-Host "      $($i.name): $state ($($i.detail))"
    }
    $model = $h.integrations | Where-Object { $_.name -eq 'interview-model' }
    if ($model -and -not $model.configured) {
        Warn 'interview-model is DEGRADED: the scripted mock answers, not a real model (expected without anthropic-api-key)'
    }
} else {
    Fail "interview-service /health answered $(if ($r.Code) { $r.Code } else { 'no answer' }) (000 = no connection; is the AppHost running, and is -InterviewUrl its port?)"
}

# 2. web: a dedicated probe, not /, which would pass on a broken bundle.
$r = Invoke-Api 'GET' "$WebUrl/healthz"
if ($r.Code -eq 200 -and ($r.Body | ConvertFrom-Json).status -eq 'ok') { Pass 'web /healthz answered 200 with status ok' }
else { Fail "web /healthz answered $(if ($r.Code) { $r.Code } else { 'no answer' }) (000 = no connection; is the web app up on $WebUrl?)" }

# 3. authservice: readiness (database migrated, signing key loaded).
$r = Invoke-Api 'GET' "$AuthUrl/health/ready"
if ($r.Code -eq 200) { Pass 'authservice /health/ready answered 200' }
else { Fail "authservice /health/ready answered $(if ($r.Code) { $r.Code } else { 'no answer' }) (000 = no connection; image pulled? container running? see the runbook's troubleshooting)" }

# 4. optional: one scripted interview through the API. It needs a token from a login (the runbook says how).
if (-not $Token) {
    Write-Host 'SKIP  interview flow: pass -Token to run it'
} else {
    $r = Invoke-Api 'POST' "$InterviewUrl/api/v1/interviews" '{"language":"en","tenure":"1y_3y"}'
    $id = if ($r.Code -eq 201) { ($r.Body | ConvertFrom-Json).id } else { $null }
    if ($id) {
        Pass 'start an interview: 201'
        $ended = $false
        $last = 0
        for ($n = 1; $n -le 40; $n++) {
            $r = Invoke-Api 'POST' "$InterviewUrl/api/v1/interviews/$id/reply" '{"text":"The work was fine, but the goals kept changing without notice."}'
            $last = $r.Code
            if ($r.Code -eq 409) { Start-Sleep -Seconds 1; continue }   # reply_in_progress: the previous turn is still being produced
            if ($r.Code -ne 200) { break }
            if (($r.Body | ConvertFrom-Json).ending) { $ended = $true; break }
        }
        if ($ended) { Pass 'reply until the interview ends: ended' }
        else { Fail "reply until the interview ends: last answer $last, no ending within 40 replies" }

        $resultCode = 0
        for ($n = 1; $n -le 30; $n++) {
            $r = Invoke-Api 'GET' "$InterviewUrl/api/v1/interviews/$id/result"
            $resultCode = $r.Code
            if ($resultCode -ne 409) { break }   # not_completed: the tiles are still being made
            Start-Sleep -Seconds 1
        }
        if ($resultCode -eq 200) { Pass 'read the result: 200' } else { Fail "read the result: $resultCode" }

        $r = Invoke-Api 'DELETE' "$InterviewUrl/api/v1/interviews/$id"
        if ($r.Code -eq 204) { Pass 'delete the interview: 204' } else { Fail "delete the interview: $($r.Code)" }
    } else {
        Fail "start an interview: $($r.Code) (401: token wrong or expired; 402: RequireCredit is on and no credit; 429: 5 starts per hour; 503: interviews disabled)"
    }
}

Write-Host ''
if ($failures -eq 0) { Write-Host 'smoke: all checks passed'; exit 0 }
Write-Host "smoke: $failures check(s) failed"
exit 1
