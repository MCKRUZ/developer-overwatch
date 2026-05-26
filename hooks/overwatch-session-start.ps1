# hooks/overwatch-session-start.ps1
# Claude Code SessionStart hook — creates a new session in Overwatch
# Reads OVERWATCH_API_URL from ~/.overwatch/config.json

$ErrorActionPreference = "SilentlyContinue"

$configPath = Join-Path $env:USERPROFILE ".overwatch" "config.json"
if (-not (Test-Path $configPath)) { exit 0 }

$config = Get-Content $configPath -Raw | ConvertFrom-Json
$apiUrl = $config.apiUrl
if (-not $apiUrl) { exit 0 }

$sessionId = [guid]::NewGuid().ToString()
$activeSessionPath = Join-Path $env:USERPROFILE ".overwatch" "active-session"
$sessionId | Set-Content $activeSessionPath -NoNewline

$developerEmail = git config user.email 2>$null
if (-not $developerEmail) { $developerEmail = "unknown@localhost" }
$developerName = git config user.name 2>$null
$repoPath = git rev-parse --show-toplevel 2>$null

$body = @{
    sessionId = $sessionId
    developerEmail = $developerEmail
    developerName = $developerName
    repoPath = $repoPath
    startedAt = (Get-Date).ToUniversalTime().ToString("o")
} | ConvertTo-Json -Compress

try {
    $headers = @{ "Content-Type" = "application/json" }
    if ($config.apiKey) { $headers["X-Api-Key"] = $config.apiKey }
    Invoke-RestMethod -Uri "$apiUrl/api/sessions/start" -Method Post -Body $body -Headers $headers -TimeoutSec 5 | Out-Null
} catch { }
