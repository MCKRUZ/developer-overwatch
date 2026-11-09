# hooks/install-hooks.ps1
# Installs Overwatch hooks (SessionStart, PostToolUse, SessionEnd, Stop) into Claude Code
# settings and configures the local agent
# Usage: .\install-hooks.ps1 -ApiUrl http://localhost:5000 [-ApiKey your-key]

param(
    [Parameter(Mandatory)]
    [string]$ApiUrl,
    [string]$ApiKey = ""
)

$overwatchDir = Join-Path $env:USERPROFILE ".overwatch"
if (-not (Test-Path $overwatchDir)) { New-Item -ItemType Directory -Path $overwatchDir -Force | Out-Null }

$hooksDir = $PSScriptRoot

$config = @{
    apiUrl = $ApiUrl
    apiKey = $ApiKey
    pollIntervalSeconds = 5
    batchSize = 50
    gitFallbackThreshold = 100
}
$config | ConvertTo-Json | Set-Content (Join-Path $overwatchDir "config.json")

$settingsPath = Join-Path $env:USERPROFILE ".claude" "settings.json"
$settings = if (Test-Path $settingsPath) {
    Get-Content $settingsPath -Raw | ConvertFrom-Json
} else {
    @{}
}

if (-not $settings.hooks) { $settings | Add-Member -NotePropertyName hooks -NotePropertyValue @{} -Force }

# Direct dot-assignment (e.g. $settings.hooks.Stop = ...) throws when $settings.hooks is a
# PSCustomObject (as it is once loaded from an existing settings.json — only a fresh hashtable
# supports adding brand-new keys that way) and the property doesn't already exist. Add-Member
# -Force works whether the property is new or already present, so re-running this script against
# an existing settings.json (e.g. to add a new hook type later) doesn't break.
$settings.hooks | Add-Member -NotePropertyName SessionStart -Force -NotePropertyValue @(@{
    hooks = @(@{
        type = "command"
        command = "pwsh -NoProfile -File `"$hooksDir\overwatch-session-start.ps1`""
    })
})

$settings.hooks | Add-Member -NotePropertyName PostToolUse -Force -NotePropertyValue @(@{
    hooks = @(@{
        type = "command"
        command = "pwsh -NoProfile -File `"$hooksDir\overwatch-post-tool-use.ps1`""
    })
})

$settings.hooks | Add-Member -NotePropertyName SessionEnd -Force -NotePropertyValue @(@{
    hooks = @(@{
        type = "command"
        command = "pwsh -NoProfile -File `"$hooksDir\overwatch-session-end.ps1`""
    })
})

$settings.hooks | Add-Member -NotePropertyName Stop -Force -NotePropertyValue @(@{
    hooks = @(@{
        type = "command"
        command = "pwsh -NoProfile -File `"$hooksDir\overwatch-stop.ps1`""
    })
})

$settings | ConvertTo-Json -Depth 10 | Set-Content $settingsPath

Write-Host "[+] Overwatch hooks installed to $settingsPath"
Write-Host "[+] Config saved to $(Join-Path $overwatchDir 'config.json')"
Write-Host "[+] API URL: $ApiUrl"
Write-Host ""
Write-Host "Hooks now write to local SQLite queue at $(Join-Path $overwatchDir 'queue.db')"

# The agent is a standalone background process — nothing else ships queued events to the API.
# Register it as a per-user scheduled task so it auto-starts at login and survives reboots
# without anyone having to open a terminal. (The SessionStart hook also does a lazy-start check
# as a safety net in case this task isn't registered on a given machine.)
$agentCommand = Get-Command overwatch-agent -ErrorAction SilentlyContinue
if ($agentCommand) {
    $agentPath = $agentCommand.Source
    $taskName = "OverwatchAgent"
    try {
        $action = New-ScheduledTaskAction -Execute $agentPath
        $trigger = New-ScheduledTaskTrigger -AtLogOn
        $taskSettings = New-ScheduledTaskSettingsSet -Hidden -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
            -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $taskSettings -Force | Out-Null
        Write-Host "[+] Registered scheduled task '$taskName' — agent will auto-start at login"
    } catch {
        Write-Host "[!] Could not register scheduled task '$taskName': $_"
        Write-Host "    You can start the agent manually for now: overwatch-agent"
    }

    if (-not (Get-Process -Name "overwatch-agent" -ErrorAction SilentlyContinue)) {
        Start-Process -FilePath $agentPath -WindowStyle Hidden
        Write-Host "[+] Started the agent now (runs hidden; check Task Manager > Details > overwatch-agent.exe)"
    } else {
        Write-Host "[+] Agent is already running"
    }
} else {
    Write-Host "[!] overwatch-agent not found on PATH — install it first:"
    Write-Host "    dotnet tool install -g DeveloperOverwatch.Agent"
    Write-Host "    Then re-run this script to register auto-start."
}
