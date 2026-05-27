# hooks/overwatch-session-start.ps1
# Claude Code SessionStart hook — queues a session-start event locally

$ErrorActionPreference = "SilentlyContinue"

$overwatchDir = Join-Path $env:USERPROFILE ".overwatch"
if (-not (Test-Path $overwatchDir)) { New-Item -ItemType Directory -Path $overwatchDir -Force | Out-Null }

$sessionId = [guid]::NewGuid().ToString()
$sessionId | Set-Content (Join-Path $overwatchDir "active-session") -NoNewline

$developerEmail = git config user.email 2>$null
if (-not $developerEmail) { $developerEmail = "unknown@localhost" }
$developerName = git config user.name 2>$null
$repoPath = git rev-parse --show-toplevel 2>$null

if ($repoPath) {
    $repoPath | Set-Content (Join-Path $overwatchDir "active-repo") -NoNewline
}

$payload = @{
    sessionId = $sessionId
    developerEmail = $developerEmail
    developerName = $developerName
    repoPath = $repoPath
    startedAt = (Get-Date).ToUniversalTime().ToString("o")
} | ConvertTo-Json -Compress

$dbPath = Join-Path $overwatchDir "queue.db"
$eventId = [guid]::NewGuid().ToString()
$now = (Get-Date).ToUniversalTime().ToString("o")

try {
    Add-Type -Path ([System.IO.Path]::Combine([System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory(), "Microsoft.Data.Sqlite.dll")) -ErrorAction SilentlyContinue
} catch { }

$escapedPayload = $payload.Replace("'", "''")

try {
    $connStr = "Data Source=$dbPath"
    $conn = New-Object Microsoft.Data.Sqlite.SqliteConnection($connStr)
    $conn.Open()
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "CREATE TABLE IF NOT EXISTS events (rowid INTEGER PRIMARY KEY, id TEXT UNIQUE, session_id TEXT, event_type TEXT, payload_json TEXT, created_at TEXT, retry_count INTEGER DEFAULT 0, shipped INTEGER DEFAULT 0)"
    $cmd.ExecuteNonQuery() | Out-Null
    $cmd.CommandText = "INSERT OR IGNORE INTO events (id, session_id, event_type, payload_json, created_at) VALUES ('$eventId', '$sessionId', 'SessionStart', '$escapedPayload', '$now')"
    $cmd.ExecuteNonQuery() | Out-Null
    $conn.Close()
} catch {
    # Fallback: write to flat file if SQLite unavailable
    $fallbackFile = Join-Path $overwatchDir "queue-fallback.jsonl"
    $line = @{ id = $eventId; sessionId = $sessionId; eventType = "SessionStart"; payload = $payload; createdAt = $now } | ConvertTo-Json -Compress
    $line | Add-Content $fallbackFile
}
