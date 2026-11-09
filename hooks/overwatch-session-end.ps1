# hooks/overwatch-session-end.ps1
# Claude Code SessionEnd hook — queues session-end event locally
#
# Session state is keyed by Claude Code's own session_id (from hook stdin JSON), not a single
# shared file — otherwise concurrent Claude Code sessions on the same machine misattribute
# events to each other.

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

$payload = @{
    sessionId = $sessionId
    endedAt = (Get-Date).ToUniversalTime().ToString("o")
} | ConvertTo-Json -Compress

# Hooks run under pwsh, which cannot load the SQLite library, so they only append to this file;
# the Agent imports it into its queue on each poll.
$eventId = [guid]::NewGuid().ToString()
$now = (Get-Date).ToUniversalTime().ToString("o")
$fallbackFile = Join-Path $overwatchDir "queue-fallback.jsonl"
$line = @{ id = $eventId; sessionId = $sessionId; eventType = "SessionEnd"; payload = $payload; createdAt = $now } | ConvertTo-Json -Compress
try { . (Join-Path $PSScriptRoot "overwatch-common.ps1"); Add-OverwatchLine $fallbackFile $line } catch { }

# Intentionally NOT deleting $mappingPath — it must persist so a later `/resume` of this same
# Claude session can find it and reactivate the same overwatch session (see
# overwatch-session-start.ps1) instead of minting a new one.
