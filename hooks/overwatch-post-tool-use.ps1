# hooks/overwatch-post-tool-use.ps1
# Claude Code PostToolUse hook — queues tool execution events locally

$ErrorActionPreference = "SilentlyContinue"

$overwatchDir = Join-Path $env:USERPROFILE ".overwatch"
$activeSessionPath = Join-Path $overwatchDir "active-session"
if (-not (Test-Path $activeSessionPath)) { exit 0 }
$sessionId = Get-Content $activeSessionPath -Raw

$toolName = $env:CLAUDE_TOOL_NAME
if (-not $toolName) { exit 0 }

$inputParams = if ($env:CLAUDE_TOOL_INPUT) {
    $env:CLAUDE_TOOL_INPUT.Substring(0, [Math]::Min($env:CLAUDE_TOOL_INPUT.Length, 1024))
} else { $null }

$outputSummary = if ($env:CLAUDE_TOOL_OUTPUT) {
    $env:CLAUDE_TOOL_OUTPUT.Substring(0, [Math]::Min($env:CLAUDE_TOOL_OUTPUT.Length, 2048))
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

$dbPath = Join-Path $overwatchDir "queue.db"
$eventId = [guid]::NewGuid().ToString()
$now = (Get-Date).ToUniversalTime().ToString("o")
$escapedPayload = $payload.Replace("'", "''")

try {
    $connStr = "Data Source=$dbPath"
    $conn = New-Object Microsoft.Data.Sqlite.SqliteConnection($connStr)
    $conn.Open()
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "INSERT OR IGNORE INTO events (id, session_id, event_type, payload_json, created_at) VALUES ('$eventId', '$sessionId', 'ToolExecution', '$escapedPayload', '$now')"
    $cmd.ExecuteNonQuery() | Out-Null
    $conn.Close()
} catch {
    $fallbackFile = Join-Path $overwatchDir "queue-fallback.jsonl"
    $line = @{ id = $eventId; sessionId = $sessionId; eventType = "ToolExecution"; payload = $payload; createdAt = $now } | ConvertTo-Json -Compress
    $line | Add-Content $fallbackFile
}
