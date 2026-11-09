# hooks/overwatch-prompt-submit.ps1
# Claude Code UserPromptSubmit hook — the user just sent a prompt, so the session is active.
#
# Privacy: the hook input carries the prompt text. This hook reads session_id only and never
# reads, stores or forwards the prompt.
#
# Runs as an async hook: it must never write to stdout, and it always exits 0.

$ErrorActionPreference = "SilentlyContinue"

try {
    $overwatchDir = Join-Path $env:USERPROFILE ".overwatch"

    $hookInput = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $claudeSessionId = $hookInput.session_id
    if (-not $claudeSessionId) { exit 0 }

    $mappingPath = Join-Path $overwatchDir "sessions\$claudeSessionId.json"
    if (-not (Test-Path $mappingPath)) { exit 0 }
    $sessionId = (Get-Content $mappingPath -Raw | ConvertFrom-Json).ourSessionId
    if (-not $sessionId) { exit 0 }

    $now = (Get-Date).ToUniversalTime().ToString("o")
    $payload = [ordered]@{ state = "active"; at = $now } | ConvertTo-Json -Compress
    $line = [ordered]@{
        id = [guid]::NewGuid().ToString()
        sessionId = $sessionId
        eventType = "ActivityChanged"
        payload = $payload
        createdAt = $now
    } | ConvertTo-Json -Compress
    . (Join-Path $PSScriptRoot "overwatch-common.ps1")
    Add-OverwatchLine (Join-Path $overwatchDir "queue-fallback.jsonl") $line
} catch { }

exit 0
