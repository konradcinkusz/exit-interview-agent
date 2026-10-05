# One-command onboarding for Windows (REPO-BASELINE §3). Mirrors scripts/setup.sh.
#   scripts/setup.ps1           run all steps
#   scripts/setup.ps1 -Check    report what is missing, change nothing
param([switch]$Check)
$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$apphost = Join-Path $repoRoot 'src/ExitInterviewAgent.AppHost'
$missing = 0

function Need($name, $pointer) {
    if (Get-Command $name -ErrorAction SilentlyContinue) { Write-Host "  ok       $name" }
    else { Write-Host "  MISSING  $name -> $pointer"; $script:missing++ }
}

Write-Host '1/4 prerequisites'
Need git 'https://git-scm.com/downloads'
Need dotnet '.NET SDK 10: https://dotnet.microsoft.com/download/dotnet/10.0'
Need node 'Node 22+: https://nodejs.org/en/download'
Need pnpm 'npm install -g pnpm   (or: corepack enable)'
$dockerOk = $false
if (Get-Command docker -ErrorAction SilentlyContinue) { docker info *> $null; $dockerOk = ($LASTEXITCODE -eq 0) }
if ($dockerOk) { Write-Host '  ok       container engine (docker)' }
else { Write-Host '  MISSING  container engine -> https://docs.docker.com/get-docker/ (needed by the AppHost and image builds)'; $missing++ }

if ($Check) { Write-Host "`ncheck: $missing item(s) missing"; exit ([int]($missing -gt 0)) }
if ($missing -gt 0) { Write-Host "`nInstall the missing prerequisites above and re-run."; exit 1 }

Write-Host '2/4 git hooks (secret scan before every commit)'
git -C $repoRoot config core.hooksPath scripts/hooks
Write-Host '  core.hooksPath = scripts/hooks'

Write-Host '3/4 local secret store (dotnet user-secrets, never files in the tree)'
dotnet user-secrets init --project $apphost *> $null
$existing = dotnet user-secrets list --project $apphost 2>$null | Select-String '^Parameters:authservice-jwt-private-key'
if ($existing) { Write-Host '  authservice dev signing key already present' }
else {
    # The platform runtime emits the PEM itself: no openssl needed on Windows.
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    $pem = $rsa.ExportPkcs8PrivateKeyPem()
    dotnet user-secrets set 'Parameters:authservice-jwt-private-key' $pem --project $apphost | Out-Null
    Write-Host '  generated a DEV-ONLY RSA-2048 signing key (PKCS#8) for the local authservice instance'
}

# MCP connector secrets (ADR-0012): used only when Mcp:AuthPublicBaseUrl and Mcp:ResourceUrl are configured.
$secretsList = dotnet user-secrets list --project $apphost 2>$null
if (-not ($secretsList | Select-String '^Parameters:authservice-mcp-client-secret')) {
    $hex = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLower()
    dotnet user-secrets set 'Parameters:authservice-mcp-client-secret' $hex --project $apphost | Out-Null
    Write-Host '  generated a DEV-ONLY MCP client secret (256 random bits)'
}
if (-not ($secretsList | Select-String '^Parameters:authservice-encryption-key')) {
    $b64 = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    dotnet user-secrets set 'Parameters:authservice-encryption-key' $b64 --project $apphost | Out-Null
    Write-Host '  generated a DEV-ONLY authservice token-encryption key (256 random bits)'
}

Write-Host '4/4 optional integrations'
Write-Host '  (optional - needed for a real model) none are wired yet: model providers arrive with the interview agent.'
Write-Host "`nDone. Start the stack:   dotnet run --project src/ExitInterviewAgent.AppHost"
Write-Host 'Troubleshooting table:   scripts/README.md'
