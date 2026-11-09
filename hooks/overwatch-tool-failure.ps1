# hooks/overwatch-tool-failure.ps1
# Claude Code PostToolUseFailure hook — records that a tool call failed, and which tool.
#
# Privacy: the hook input carries the tool's input and the error text. This hook reads
# session_id and tool_name only and never reads, stores or forwards either of them.
#
# Runs as an async hook: it must never write to stdout, and it always exits 0.

$ErrorActionPreference = "SilentlyContinue"

# The server rejects longer tool names, and a rejected batch would be retried forever.
$maxToolNameLength = 255

try {
    $overwatchDir = Join-Path $env:USERPROFILE ".overwatch"

    $hookInput = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $claudeSessionId = $hookInput.session_id
    if ($claudeSessionId -notmatch '^[A-Za-z0-9-]{1,100}$') { exit 0 }

    $toolName = [string]$hookInput.tool_name
    if (-not $toolName) { exit 0 }
    $toolName = $toolName.Substring(0, [Math]::Min($toolName.Length, $maxToolNameLength))

    $mappingPath = Join-Path $overwatchDir "sessions\$claudeSessionId.json"
    if (-not (Test-Path $mappingPath)) { exit 0 }
    $sessionId = (Get-Content $mappingPath -Raw | ConvertFrom-Json).ourSessionId
    if (-not $sessionId) { exit 0 }

    $now = (Get-Date).ToUniversalTime().ToString("o")
    $payload = [ordered]@{ toolName = $toolName; at = $now } | ConvertTo-Json -Compress
    $line = [ordered]@{
        id = [guid]::NewGuid().ToString()
        sessionId = $sessionId
        eventType = "ToolFailed"
        payload = $payload
        createdAt = $now
    } | ConvertTo-Json -Compress
    . (Join-Path $PSScriptRoot "overwatch-common.ps1")
    Add-OverwatchLine (Join-Path $overwatchDir "queue-fallback.jsonl") $line
} catch { }

exit 0
