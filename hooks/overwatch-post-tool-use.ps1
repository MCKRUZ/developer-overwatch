# hooks/overwatch-post-tool-use.ps1
# Claude Code PostToolUse hook — queues tool execution events locally
#
# Claude Code delivers hook event data on stdin as JSON (session_id, tool_name, tool_input,
# tool_response, etc.) — not via environment variables. See:
# https://code.claude.com/docs/en/hooks
#
# Session state is keyed by Claude Code's own session_id, not a single shared file — otherwise
# concurrent Claude Code sessions on the same machine misattribute events to each other.

$ErrorActionPreference = "SilentlyContinue"

$overwatchDir = Join-Path $env:USERPROFILE ".overwatch"

$stdinJson = [Console]::In.ReadToEnd()
$hookInput = $null
try { $hookInput = $stdinJson | ConvertFrom-Json } catch { }
if (-not $hookInput) { exit 0 }

$claudeSessionId = $hookInput.session_id
if (-not $claudeSessionId) { exit 0 }

$mappingPath = Join-Path $overwatchDir "sessions\$claudeSessionId.json"
if (-not (Test-Path $mappingPath)) { exit 0 }
$mapping = Get-Content $mappingPath -Raw | ConvertFrom-Json
$sessionId = $mapping.ourSessionId

$toolName = $hookInput.tool_name
if (-not $toolName) { exit 0 }

$inputJson = if ($hookInput.tool_input) { $hookInput.tool_input | ConvertTo-Json -Depth 10 -Compress } else { $null }
$inputParams = if ($inputJson) {
    $inputJson.Substring(0, [Math]::Min($inputJson.Length, 1024))
} else { $null }

$outputJson = if ($hookInput.tool_response) { $hookInput.tool_response | ConvertTo-Json -Depth 10 -Compress } else { $null }
$outputSummary = if ($outputJson) {
    $outputJson.Substring(0, [Math]::Min($outputJson.Length, 2048))
} else { $null }

$toolExecId = [guid]::NewGuid().ToString()
$payload = @{
    sessionId = $sessionId
    toolExecutions = @(@{
        id = $toolExecId
        toolName = $toolName
        inputParams = $inputParams
        outputSummary = $outputSummary
        timestamp = (Get-Date).ToUniversalTime().ToString("o")
    })
    fileChanges = @()
    conversationTurns = @()
} | ConvertTo-Json -Depth 4 -Compress

# Hooks run under pwsh, which cannot load the SQLite library, so they only append to this file;
# the Agent imports it into its queue on each poll.
$eventId = [guid]::NewGuid().ToString()
$now = (Get-Date).ToUniversalTime().ToString("o")
$fallbackFile = Join-Path $overwatchDir "queue-fallback.jsonl"
$line = @{ id = $eventId; sessionId = $sessionId; eventType = "ToolExecution"; payload = $payload; createdAt = $now } | ConvertTo-Json -Compress
try { $line | Add-Content $fallbackFile } catch { }
exit 0
