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
#
# Registered without async: every other hook drops its event until this mapping exists.

$ErrorActionPreference = "SilentlyContinue"

$stdinJson = [Console]::In.ReadToEnd()
$hookInput = $null
try { $hookInput = $stdinJson | ConvertFrom-Json } catch { }
$claudeSessionId = if ($hookInput) { $hookInput.session_id } else { $null }
# The id names a file, so anything that could leave the sessions folder is refused.
if ($claudeSessionId -notmatch '^[A-Za-z0-9-]{1,100}$') { exit 0 }

# Taken first, before the lazy agent start, the git calls and the title lookup, so the start errs early: a
# first prompt typed right after a resume is not dated before it and dropped. It is still taken before the
# mapping is written: once that exists, other hooks can queue events, and none of them may sort before this start.
$now = (Get-Date).ToUniversalTime().ToString("o")

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

# SessionStart fires again on `/resume` (and on `/clear`, `/compact`) for a Claude session that
# already has a mapping here — reuse the existing overwatch session ID rather than minting a new
# one, and still send SessionStart so the API reactivates it. The source goes with it, and decides
# whether the start may open the session for shipping: startup, resume or fork can; clear, and a missing
# source, which is sent as null (as from a hook that predates the field), open only a session the agent
# has never decided on; compact never does. A source that is not a plain word goes as "unknown", which
# never opens a session.
$rawSource = if ($hookInput) { $hookInput.source } else { $null }
$source = if ($null -eq $rawSource) { $null }
    elseif ($rawSource -is [string] -and $rawSource.ToLowerInvariant() -cmatch '\A[a-z_]{1,32}\z') { $rawSource.ToLowerInvariant() }
    else { 'unknown' }
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
# When it was written goes too: the agent keeps a title from before enrolment on this machine.
$title = $null
$titleWrittenAt = $null
$transcriptPath = if ($hookInput) { $hookInput.transcript_path } else { $null }
if ($transcriptPath -and (Test-Path $transcriptPath)) {
    $sessionFolder = [System.IO.Path]::GetFileNameWithoutExtension($transcriptPath)
    $titlePath = Join-Path (Split-Path $transcriptPath -Parent) "$sessionFolder\custom-title.json"
    if (Test-Path $titlePath) {
        try {
            $titleWrittenAt = (Get-Item $titlePath).LastWriteTimeUtc.ToString("o")
            $title = (Get-Content $titlePath -Raw | ConvertFrom-Json).customTitle
        } catch { }
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
    titleWrittenAt = $titleWrittenAt
    source = $source
    startedAt = $now
} | ConvertTo-Json -Compress

# Hooks run under pwsh, which cannot load the SQLite library, so they only append to this file;
# the Agent imports it into its queue on each poll.
$eventId = [guid]::NewGuid().ToString()
$fallbackFile = Join-Path $overwatchDir "queue-fallback.jsonl"
$line = @{ id = $eventId; sessionId = $sessionId; eventType = "SessionStart"; payload = $payload; createdAt = $now } | ConvertTo-Json -Compress
try { . (Join-Path $PSScriptRoot "overwatch-common.ps1"); Add-OverwatchLine $fallbackFile $line } catch { }
exit 0
