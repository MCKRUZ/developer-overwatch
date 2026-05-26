# hooks/overwatch-post-tool-use.ps1
# Claude Code PostToolUse hook — sends tool execution data to Overwatch
# Receives tool info via CLAUDE_TOOL_NAME, CLAUDE_TOOL_INPUT, CLAUDE_TOOL_OUTPUT env vars

$ErrorActionPreference = "SilentlyContinue"

$activeSessionPath = Join-Path $env:USERPROFILE ".overwatch" "active-session"
if (-not (Test-Path $activeSessionPath)) { exit 0 }
$sessionId = Get-Content $activeSessionPath -Raw

$configPath = Join-Path $env:USERPROFILE ".overwatch" "config.json"
if (-not (Test-Path $configPath)) { exit 0 }
$config = Get-Content $configPath -Raw | ConvertFrom-Json
$apiUrl = $config.apiUrl
if (-not $apiUrl) { exit 0 }

$toolName = $env:CLAUDE_TOOL_NAME
if (-not $toolName) { exit 0 }

$inputParams = if ($env:CLAUDE_TOOL_INPUT) {
    $env:CLAUDE_TOOL_INPUT.Substring(0, [Math]::Min($env:CLAUDE_TOOL_INPUT.Length, 1024))
} else { $null }

$outputSummary = if ($env:CLAUDE_TOOL_OUTPUT) {
    $env:CLAUDE_TOOL_OUTPUT.Substring(0, [Math]::Min($env:CLAUDE_TOOL_OUTPUT.Length, 2048))
} else { $null }

$body = @{
    sessionId = $sessionId
    toolExecutions = @(@{
        id = [guid]::NewGuid().ToString()
        toolName = $toolName
        inputParams = $inputParams
        outputSummary = $outputSummary
        timestamp = (Get-Date).ToUniversalTime().ToString("o")
    })
    fileChanges = @()
    conversationTurns = @()
} | ConvertTo-Json -Depth 4 -Compress

try {
    $headers = @{ "Content-Type" = "application/json" }
    if ($config.apiKey) { $headers["X-Api-Key"] = $config.apiKey }
    Invoke-RestMethod -Uri "$apiUrl/api/sessions/$sessionId/events" -Method Post -Body $body -Headers $headers -TimeoutSec 5 | Out-Null
} catch { }
