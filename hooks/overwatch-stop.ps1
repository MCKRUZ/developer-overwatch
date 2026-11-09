# hooks/overwatch-stop.ps1
# Claude Code Stop hook — fires once per assistant turn. Queues a lightweight marker event
# carrying the transcript_path so the Agent can parse newly-appeared conversation turns
# (with token usage) out of Claude Code's own transcript file.
#
# Claude Code delivers hook event data on stdin as JSON. See:
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

$transcriptPath = $hookInput.transcript_path
if (-not $transcriptPath) { exit 0 }

$payload = @{
    sessionId = $sessionId
    transcriptPath = $transcriptPath
} | ConvertTo-Json -Compress

$dbPath = Join-Path $overwatchDir "queue.db"
$eventId = [guid]::NewGuid().ToString()
$now = (Get-Date).ToUniversalTime().ToString("o")
$escapedPayload = $payload.Replace("'", "''")

try {
    $connStr = "Data Source=$dbPath"
    $conn = New-Object Microsoft.Data.Sqlite.SqliteConnection($connStr)
    $conn.Open()
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "INSERT OR IGNORE INTO events (id, session_id, event_type, payload_json, created_at) VALUES ('$eventId', '$sessionId', 'ConversationTurn', '$escapedPayload', '$now')"
    $cmd.ExecuteNonQuery() | Out-Null
    $conn.Close()
} catch {
    $fallbackFile = Join-Path $overwatchDir "queue-fallback.jsonl"
    $line = @{ id = $eventId; sessionId = $sessionId; eventType = "ConversationTurn"; payload = $payload; createdAt = $now } | ConvertTo-Json -Compress
    $line | Add-Content $fallbackFile
}
