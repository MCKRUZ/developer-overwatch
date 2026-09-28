# hooks/overwatch-session-start.ps1
# Claude Code SessionStart hook — queues a session-start event locally
#
# Session state is keyed by Claude Code's own session_id (from hook stdin JSON), not a single
# shared file — otherwise concurrent Claude Code sessions on the same machine stomp on each
# other's "active session" marker and events get misattributed across sessions.
#
# The per-session mapping file persists indefinitely (never deleted on SessionEnd) so that a
# later `/resume` of the same Claude session — even much later — is recognized as the same
# session and reactivates it (via the API's upsert-on-conflict SessionStart handling) instead of
# creating a duplicate.

$ErrorActionPreference = "SilentlyContinue"

# Safety net: the agent is meant to auto-start via the "OverwatchAgent" scheduled task
# (registered by install-hooks.ps1), but if that task isn't registered on this machine —
# or was killed and hasn't restarted yet — events would otherwise silently pile up in the
# local queue with nothing shipping them to the API. Lazily start it here instead.
if (-not (Get-Process -Name "overwatch-agent" -ErrorAction SilentlyContinue)) {
    $agentCommand = Get-Command overwatch-agent -ErrorAction SilentlyContinue
    if ($agentCommand) {
        Start-Process -FilePath $agentCommand.Source -WindowStyle Hidden
    }
}

$overwatchDir = Join-Path $env:USERPROFILE ".overwatch"
if (-not (Test-Path $overwatchDir)) { New-Item -ItemType Directory -Path $overwatchDir -Force | Out-Null }

$sessionsDir = Join-Path $overwatchDir "sessions"
if (-not (Test-Path $sessionsDir)) { New-Item -ItemType Directory -Path $sessionsDir -Force | Out-Null }

$stdinJson = [Console]::In.ReadToEnd()
$hookInput = $null
try { $hookInput = $stdinJson | ConvertFrom-Json } catch { }
$claudeSessionId = if ($hookInput) { $hookInput.session_id } else { $null }
if (-not $claudeSessionId) { exit 0 }

# SessionStart fires again on `/resume` (and on `/clear`, `/compact`) for a Claude session that
# already has a mapping here — reuse the existing overwatch session ID rather than minting a new
# one, and still send SessionStart so the API reactivates it.
$mappingPath = Join-Path $sessionsDir "$claudeSessionId.json"
$existingMapping = if (Test-Path $mappingPath) { Get-Content $mappingPath -Raw | ConvertFrom-Json } else { $null }

$sessionId = if ($existingMapping) { $existingMapping.ourSessionId } else { [guid]::NewGuid().ToString() }

$developerEmail = git config user.email 2>$null
if (-not $developerEmail) { $developerEmail = "unknown@localhost" }
$developerName = git config user.name 2>$null
$repoPath = git rev-parse --show-toplevel 2>$null

# A `/resume`d session may already have a `/rename`d title sitting in Claude Code's own sidecar
# file (<projects-dir>/<encoded-repo>/<session-id>/custom-title.json) — surface it immediately
# instead of waiting for the first captured turn to pick it up.
$title = $null
$transcriptPath = if ($hookInput) { $hookInput.transcript_path } else { $null }
if ($transcriptPath -and (Test-Path $transcriptPath)) {
    $sessionFolder = [System.IO.Path]::GetFileNameWithoutExtension($transcriptPath)
    $titlePath = Join-Path (Split-Path $transcriptPath -Parent) "$sessionFolder\custom-title.json"
    if (Test-Path $titlePath) {
        try { $title = (Get-Content $titlePath -Raw | ConvertFrom-Json).customTitle } catch { }
    }
}

$mapping = @{
    ourSessionId = $sessionId
    repoPath = $repoPath
} | ConvertTo-Json -Compress
$mapping | Set-Content $mappingPath

$payload = @{
    sessionId = $sessionId
    developerEmail = $developerEmail
    developerName = $developerName
    repoPath = $repoPath
    title = $title
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
