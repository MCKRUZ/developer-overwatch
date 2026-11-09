# hooks/overwatch-notification.ps1
# Claude Code Notification hook — maps notification_type to the session's activity state:
#   permission_prompt, elicitation_dialog, elicitation_url_dialog, agent_needs_input -> waiting
#   idle_prompt (turn finished about 60 s ago and the user has not typed)            -> idle
# Every other notification type is ignored.
#
# Runs as an async hook: it must never write to stdout, and it always exits 0.

$ErrorActionPreference = "SilentlyContinue"

$waitingTypes = @("permission_prompt", "elicitation_dialog", "elicitation_url_dialog", "agent_needs_input")

try {
    $overwatchDir = Join-Path $env:USERPROFILE ".overwatch"

    $hookInput = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $claudeSessionId = $hookInput.session_id
    if (-not $claudeSessionId) { exit 0 }

    $notificationType = [string]$hookInput.notification_type
    $state = if ($waitingTypes -contains $notificationType) { "waiting" }
             elseif ($notificationType -eq "idle_prompt") { "idle" }
             else { $null }
    if (-not $state) { exit 0 }

    $mappingPath = Join-Path $overwatchDir "sessions\$claudeSessionId.json"
    if (-not (Test-Path $mappingPath)) { exit 0 }
    $sessionId = (Get-Content $mappingPath -Raw | ConvertFrom-Json).ourSessionId
    if (-not $sessionId) { exit 0 }

    $now = (Get-Date).ToUniversalTime().ToString("o")
    $payload = [ordered]@{ state = $state; at = $now } | ConvertTo-Json -Compress
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
