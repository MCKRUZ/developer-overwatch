# hooks/overwatch-session-end.ps1
# Claude Code SessionEnd hook — finalizes the session in Overwatch

$ErrorActionPreference = "SilentlyContinue"

$activeSessionPath = Join-Path $env:USERPROFILE ".overwatch" "active-session"
if (-not (Test-Path $activeSessionPath)) { exit 0 }
$sessionId = Get-Content $activeSessionPath -Raw

$configPath = Join-Path $env:USERPROFILE ".overwatch" "config.json"
if (-not (Test-Path $configPath)) { exit 0 }
$config = Get-Content $configPath -Raw | ConvertFrom-Json
$apiUrl = $config.apiUrl
if (-not $apiUrl) { exit 0 }

$body = @{
    sessionId = $sessionId
    endedAt = (Get-Date).ToUniversalTime().ToString("o")
} | ConvertTo-Json -Compress

try {
    $headers = @{ "Content-Type" = "application/json" }
    if ($config.apiKey) { $headers["X-Api-Key"] = $config.apiKey }
    Invoke-RestMethod -Uri "$apiUrl/api/sessions/$sessionId/end" -Method Post -Body $body -Headers $headers -TimeoutSec 5 | Out-Null
} catch { }

Remove-Item $activeSessionPath -Force 2>$null
