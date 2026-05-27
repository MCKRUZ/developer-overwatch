# hooks/overwatch-session-end.ps1
# Claude Code SessionEnd hook — queues session-end event locally

$ErrorActionPreference = "SilentlyContinue"

$overwatchDir = Join-Path $env:USERPROFILE ".overwatch"
$activeSessionPath = Join-Path $overwatchDir "active-session"
if (-not (Test-Path $activeSessionPath)) { exit 0 }
$sessionId = Get-Content $activeSessionPath -Raw

$payload = @{
    sessionId = $sessionId
    endedAt = (Get-Date).ToUniversalTime().ToString("o")
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
    $cmd.CommandText = "INSERT OR IGNORE INTO events (id, session_id, event_type, payload_json, created_at) VALUES ('$eventId', '$sessionId', 'SessionEnd', '$escapedPayload', '$now')"
    $cmd.ExecuteNonQuery() | Out-Null
    $conn.Close()
} catch {
    $fallbackFile = Join-Path $overwatchDir "queue-fallback.jsonl"
    $line = @{ id = $eventId; sessionId = $sessionId; eventType = "SessionEnd"; payload = $payload; createdAt = $now } | ConvertTo-Json -Compress
    $line | Add-Content $fallbackFile
}

Remove-Item $activeSessionPath -Force 2>$null
Remove-Item (Join-Path $overwatchDir "active-repo") -Force 2>$null
