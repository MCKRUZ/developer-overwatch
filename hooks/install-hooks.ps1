#Requires -Version 7
# hooks/install-hooks.ps1
# Registers the Overwatch hooks in Claude Code's settings and writes the agent's config.
#
# Usage:
#   .\install-hooks.ps1                                        # local: usage stays on this machine
#   .\install-hooks.ps1 -Pilot -ApiUrl <url> [-ApiKey <key>]   # pilot: also ships to the Overwatch API
#   add -NoStart to skip registering the scheduled task and starting the agent
#
# With neither -Pilot nor -Local, a fresh install is local and a re-run keeps the existing mode.
# A config with no "mode" key predates modes and has always shipped, so it stays pilot, which is
# also how the agent reads it. Other config keys are kept.
#
# Hooks are merged into ~/.claude/settings.json: every hook and setting that is not Overwatch's is
# kept, entries whose command names an overwatch- script are replaced, and the file as it was
# before the first change is kept as settings.json.overwatch-backup.

param(
    [switch]$Pilot,
    [switch]$Local,
    [string]$ApiUrl,
    [string]$ApiKey,
    [switch]$NoStart
)

$ErrorActionPreference = "Stop"

# All async: a cold pwsh costs about 440 ms, and an async command hook runs in the background
# instead of holding up Claude Code.
$hookScripts = [ordered]@{
    SessionStart       = "overwatch-session-start.ps1"
    PostToolUse        = "overwatch-post-tool-use.ps1"
    PostToolUseFailure = "overwatch-tool-failure.ps1"
    UserPromptSubmit   = "overwatch-prompt-submit.ps1"
    Notification       = "overwatch-notification.ps1"
    Stop               = "overwatch-stop.ps1"
    SessionEnd         = "overwatch-session-end.ps1"
}

$configDefaults = [ordered]@{ pollIntervalSeconds = 5; batchSize = 50; gitFallbackThreshold = 100 }

function Stop-Install([string]$Message) {
    [Console]::Error.WriteLine("[x] $Message")
    [Console]::Error.WriteLine("    Nothing was changed.")
    exit 1
}

# Null when the file does not exist.
function Read-JsonObject([string]$Path) {
    if (-not (Test-Path $Path)) { return $null }
    $raw = Get-Content $Path -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) { return [ordered]@{} }
    try {
        $value = $raw | ConvertFrom-Json -AsHashtable
    } catch {
        Stop-Install "$Path is not valid JSON. Fix or remove it, then run this again."
    }
    if ($value -isnot [System.Collections.IDictionary]) {
        Stop-Install "$Path does not hold a JSON object. Fix or remove it, then run this again."
    }
    return $value
}

function Test-OverwatchHook($Hook) {
    $Hook -is [System.Collections.IDictionary] -and ([string]$Hook["command"]) -match "overwatch-"
}

# Removes every hook whose command names an overwatch- script, from every event, and drops the
# groups and events left empty. A group can hold a user's hook beside ours; the user's stays.
function Remove-OverwatchHooks([System.Collections.IDictionary]$Hooks) {
    foreach ($eventName in @($Hooks.Keys)) {
        $groups = @(foreach ($group in @($Hooks[$eventName])) {
            $inner = if ($group -is [System.Collections.IDictionary] -and $null -ne $group["hooks"]) { @($group["hooks"]) } else { @() }
            $kept = @($inner | Where-Object { -not (Test-OverwatchHook $_) })
            if ($kept.Count -eq $inner.Count) { $group; continue }
            if ($kept.Count -eq 0) { continue }
            $group["hooks"] = $kept
            $group
        })
        if ($groups.Count -eq 0) { $Hooks.Remove($eventName) } else { $Hooks[$eventName] = $groups }
    }
}

function Add-OverwatchHooks([System.Collections.IDictionary]$Hooks) {
    foreach ($eventName in $hookScripts.Keys) {
        $command = "pwsh -NoProfile -File `"$(Join-Path $PSScriptRoot $hookScripts[$eventName])`""
        $groups = [System.Collections.Generic.List[object]]::new()
        foreach ($existing in @($Hooks[$eventName])) {
            if ($null -ne $existing) { $groups.Add($existing) }
        }
        $groups.Add([ordered]@{ hooks = @([ordered]@{ type = "command"; command = $command; async = $true }) })
        $Hooks[$eventName] = $groups.ToArray()
    }
}

# --- Read and validate everything before writing anything ---

if ($Pilot -and $Local) { Stop-Install "Pass -Pilot or -Local, not both." }

$overwatchDir = Join-Path $env:USERPROFILE ".overwatch"
$configPath = Join-Path $overwatchDir "config.json"
$claudeDir = Join-Path $env:USERPROFILE ".claude"
$settingsPath = Join-Path $claudeDir "settings.json"
$backupPath = "$settingsPath.overwatch-backup"

$existingConfig = Read-JsonObject $configPath
$config = if ($null -eq $existingConfig) { [ordered]@{} } else { $existingConfig }

$mode = if ($Pilot) { "pilot" }
    elseif ($Local) { "local" }
    elseif ($null -eq $existingConfig) { "local" }
    elseif (-not $config.Contains("mode")) { "pilot" }
    elseif ([string]$config["mode"] -eq "pilot") { "pilot" }
    else { "local" }

$droppedServer = $false
if ($mode -eq "pilot") {
    if ($ApiUrl) { $config["apiUrl"] = $ApiUrl }
    if (-not $config["apiUrl"]) {
        Stop-Install "Pilot mode needs -ApiUrl, the Overwatch API this machine ships usage to. Example: .\install-hooks.ps1 -Pilot -ApiUrl https://overwatch.example.com"
    }
    if ($PSBoundParameters.ContainsKey("ApiKey")) { $config["apiKey"] = $ApiKey }
} else {
    if ($ApiUrl -or $PSBoundParameters.ContainsKey("ApiKey")) {
        Stop-Install "-ApiUrl and -ApiKey apply only to pilot mode. Add -Pilot to ship usage to that API."
    }
    # Local mode never ships, so the config keeps no server address or key.
    $droppedServer = $config.Contains("apiUrl") -or $config.Contains("apiKey")
    $config.Remove("apiUrl")
    $config.Remove("apiKey")
}

$settings = Read-JsonObject $settingsPath
$settingsExisted = $null -ne $settings
if (-not $settingsExisted) { $settings = [ordered]@{} }
if ($null -eq $settings["hooks"]) {
    $settings["hooks"] = [ordered]@{}
} elseif ($settings["hooks"] -isnot [System.Collections.IDictionary]) {
    Stop-Install "The `"hooks`" entry in $settingsPath is not a JSON object. Fix it, then run this again."
}

# --- Write ---

$config["mode"] = $mode
foreach ($key in $configDefaults.Keys) {
    if (-not $config.Contains($key)) { $config[$key] = $configDefaults[$key] }
}
New-Item -ItemType Directory -Path $overwatchDir -Force | Out-Null
$config | ConvertTo-Json -Depth 100 | Set-Content $configPath

New-Item -ItemType Directory -Path $claudeDir -Force | Out-Null
if ($settingsExisted -and -not (Test-Path $backupPath)) { Copy-Item $settingsPath $backupPath }
Remove-OverwatchHooks $settings["hooks"]
Add-OverwatchHooks $settings["hooks"]
$settings | ConvertTo-Json -Depth 100 | Set-Content $settingsPath

Write-Host "[+] Overwatch hooks merged into $settingsPath"
if (Test-Path $backupPath) { Write-Host "[+] Settings before Overwatch first changed them: $backupPath" }
Write-Host "[+] Config saved to $configPath"
if ($mode -eq "pilot") {
    Write-Host "[+] Mode: pilot. Usage is recorded here and shipped to $($config["apiUrl"])"
} else {
    Write-Host "[+] Mode: local. Usage is recorded on this machine only"
    if ($droppedServer) { Write-Host "[+] Removed the API address and key, which local mode does not use" }
}

if ($NoStart) {
    Write-Host "[+] -NoStart: did not register the scheduled task or start the agent"
    exit 0
}

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
