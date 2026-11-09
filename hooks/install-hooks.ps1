#Requires -Version 7.5
# hooks/install-hooks.ps1
# Registers the Overwatch hooks in Claude Code's settings and writes the agent's config.
#
# Usage:
#   .\install-hooks.ps1                                        # local: usage stays on this machine
#   .\install-hooks.ps1 -Pilot -ApiUrl <url> [-ApiKey <key>]   # pilot: also ships to the Overwatch API
#   add -NoStart to skip registering the scheduled task and starting the agent
#   add -NoProcessControl to never stop or start an agent process (tests use it)
#
# With neither -Pilot nor -Local, a fresh install is local and a re-run keeps the existing mode.
# A config with no "mode" key predates modes and has always shipped, so it stays pilot, which is
# also how the agent reads it. Other config keys are kept.
#
# A running agent holds its config in memory. When this run changes the mode, the API address or
# the key, the running agent is stopped before config.json is written, even with -NoStart, so a
# pilot agent never keeps shipping after a switch to local. It is then started again unless
# -NoStart is set.
#
# Hooks are merged into ~/.claude/settings.json: every hook and setting that is not Overwatch's is
# kept, and only entries whose command runs one of the scripts below are replaced. Each run backs
# the file up to settings.json.overwatch-backup first; the file as it was before Overwatch ever
# changed it is kept as settings.json.overwatch-backup.original and never overwritten.
#
# Needs PowerShell 7.5 for ConvertFrom-Json -DateKind String. Older versions turn date strings in
# the user's settings into dates and write them back reformatted.

param(
    [switch]$Pilot,
    [switch]$Local,
    [string]$ApiUrl,
    [string]$ApiKey,
    [switch]$NoStart,
    [switch]$NoProcessControl
)

$ErrorActionPreference = "Stop"

# Every hook but SessionStart is async: a cold pwsh costs about 440 ms, and an async command hook
# runs in the background instead of holding up Claude Code. SessionStart writes the session mapping
# every other hook needs, so it runs to completion first; otherwise a fast or headless session
# (claude -p) drops its first events. Claude Code already runs SessionStart without blocking input.
$hookScripts = [ordered]@{
    SessionStart       = "overwatch-session-start.ps1"
    PostToolUse        = "overwatch-post-tool-use.ps1"
    PostToolUseFailure = "overwatch-tool-failure.ps1"
    UserPromptSubmit   = "overwatch-prompt-submit.ps1"
    Notification       = "overwatch-notification.ps1"
    Stop               = "overwatch-stop.ps1"
    SessionEnd         = "overwatch-session-end.ps1"
}
$syncEvents = @("SessionStart")

# One of our script names at the end of a path, right before the closing quote of the -File
# argument. A user's own script such as C:\tools\overwatch-lint.ps1 does not match.
$overwatchHookPattern = '[\\/](?:' + (@($hookScripts.Values | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')"'

$configDefaults = [ordered]@{ pollIntervalSeconds = 5; batchSize = 50 }

$agentName = "overwatch-agent"

function Stop-Install([string]$Message) {
    [Console]::Error.WriteLine("[x] $Message")
    [Console]::Error.WriteLine("    Nothing was changed.")
    exit 1
}

# Null when the file does not exist. Date strings stay strings, so they are written back unchanged.
function Read-JsonObject([string]$Path) {
    if (-not (Test-Path $Path)) { return $null }
    $raw = Get-Content $Path -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) { return [ordered]@{} }
    try {
        $value = $raw | ConvertFrom-Json -AsHashtable -DateKind String
    } catch {
        Stop-Install "$Path is not valid JSON. Fix or remove it, then run this again."
    }
    if ($value -isnot [System.Collections.IDictionary]) {
        Stop-Install "$Path does not hold a JSON object. Fix or remove it, then run this again."
    }
    return $value
}

# Writes beside the file, then moves over it, so a crash never leaves a half-written file. A symlink
# (a dotfiles setup) is followed and its target replaced, so the link survives.
function Write-FileAtomic([string]$Path, [string]$Content) {
    if ((Test-Path -LiteralPath $Path) -and (Get-Item -LiteralPath $Path -Force).LinkType -eq "SymbolicLink") {
        $Path = (Get-Item -LiteralPath $Path -Force).ResolveLinkTarget($true).FullName
    }
    $tmp = "$Path.tmp"
    Set-Content -LiteralPath $tmp -Value $Content
    Move-Item -LiteralPath $tmp -Destination $Path -Force
}

function Test-OverwatchHook($Hook) {
    $Hook -is [System.Collections.IDictionary] -and ([string]$Hook["command"]) -match $overwatchHookPattern
}

# Removes every hook that runs one of our scripts, from every event, and drops the groups and
# events left empty. A group can hold a user's hook beside ours; the user's stays.
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
        $hook = [ordered]@{ type = "command"; command = "pwsh -NoProfile -File `"$(Join-Path $PSScriptRoot $hookScripts[$eventName])`"" }
        if ($eventName -notin $syncEvents) { $hook["async"] = $true }
        $groups = [System.Collections.Generic.List[object]]::new()
        foreach ($existing in @($Hooks[$eventName])) {
            if ($null -ne $existing) { $groups.Add($existing) }
        }
        $groups.Add([ordered]@{ hooks = @($hook) })
        $Hooks[$eventName] = $groups.ToArray()
    }
}

function Get-ConfigMode($Config) {
    if ($null -eq $Config) { return $null }
    if (-not $Config.Contains("mode")) { return "pilot" }
    if ([string]$Config["mode"] -eq "pilot") { return "pilot" }
    return "local"
}

# This user's agents only, which needs no admin rights.
function Get-OwnAgent {
    $session = (Get-Process -Id $PID).SessionId
    @(Get-Process -Name $agentName -ErrorAction SilentlyContinue | Where-Object SessionId -eq $session)
}

function Stop-OwnAgent {
    $running = Get-OwnAgent
    if ($running.Count -eq 0) { return $false }
    try {
        $running | Stop-Process -Force
        $running | Wait-Process -Timeout 15
    } catch {
        Stop-Install "Could not stop the running $agentName, which would keep its old settings: $_. Stop it in Task Manager, then run this again."
    }
    return $true
}

function Start-OwnAgent {
    $command = Get-Command $agentName -ErrorAction SilentlyContinue
    if (-not $command) { return $false }
    Start-Process -FilePath $command.Source -WindowStyle Hidden
    return $true
}

# A write failed after validation passed, possibly with the agent already stopped. Says what was and
# was not written instead of a raw exception, and restarts an agent whose config is still the old one.
function Stop-WriteFailed([string]$Reason, [bool]$ConfigWritten, [bool]$StoppedAgent) {
    [Console]::Error.WriteLine("[x] Could not finish the install: $Reason")
    if ($ConfigWritten) {
        [Console]::Error.WriteLine("    $configPath was updated, but $settingsPath was not. Fix the problem, then run this again.")
    } else {
        [Console]::Error.WriteLine("    Neither $configPath nor $settingsPath was changed. Fix the problem, then run this again.")
    }
    if ($StoppedAgent) {
        if (-not $ConfigWritten -and -not $NoStart -and -not $NoProcessControl -and (Start-OwnAgent)) {
            [Console]::Error.WriteLine("    Restarted the agent with its old settings.")
        } else {
            [Console]::Error.WriteLine("    The agent was stopped. It starts again with the next Claude Code session, or run $agentName.")
        }
    }
    exit 1
}

# --- Read and validate everything before writing anything ---

if ($Pilot -and $Local) { Stop-Install "Pass -Pilot or -Local, not both." }

$overwatchDir = Join-Path $env:USERPROFILE ".overwatch"
$configPath = Join-Path $overwatchDir "config.json"
$claudeDir = Join-Path $env:USERPROFILE ".claude"
$settingsPath = Join-Path $claudeDir "settings.json"
$backupPath = "$settingsPath.overwatch-backup"
$originalBackupPath = "$backupPath.original"

$existingConfig = Read-JsonObject $configPath
$config = if ($null -eq $existingConfig) { [ordered]@{} } else { $existingConfig }

# Taken before $config, the same object, is changed below.
$oldMode = Get-ConfigMode $existingConfig
$oldApiUrl = [string]$config["apiUrl"]
$oldApiKey = [string]$config["apiKey"]

$mode = if ($Pilot) { "pilot" }
    elseif ($Local) { "local" }
    elseif ($null -eq $existingConfig) { "local" }
    else { $oldMode }

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

# A fresh install has no config, so no running agent can hold an old one.
$restartNeeded = $null -ne $existingConfig -and (
    $oldMode -ne $mode -or $oldApiUrl -ne [string]$config["apiUrl"] -or $oldApiKey -ne [string]$config["apiKey"])

$settings = Read-JsonObject $settingsPath
$settingsExisted = $null -ne $settings
if (-not $settingsExisted) { $settings = [ordered]@{} }
if ($null -eq $settings["hooks"]) {
    $settings["hooks"] = [ordered]@{}
} elseif ($settings["hooks"] -isnot [System.Collections.IDictionary]) {
    Stop-Install "The `"hooks`" entry in $settingsPath is not a JSON object. Fix it, then run this again."
}

# --- Write ---

$stoppedAgent = $false
if ($restartNeeded -and -not $NoProcessControl) { $stoppedAgent = Stop-OwnAgent }

$config["mode"] = $mode
foreach ($key in $configDefaults.Keys) {
    if (-not $config.Contains($key)) { $config[$key] = $configDefaults[$key] }
}
# The git fallback is gone; an older install's key would only mislead whoever reads the config.
$config.Remove("gitFallbackThreshold")
$configWritten = $false
try {
    New-Item -ItemType Directory -Path $overwatchDir -Force | Out-Null
    Write-FileAtomic $configPath ($config | ConvertTo-Json -Depth 100)
    $configWritten = $true

    New-Item -ItemType Directory -Path $claudeDir -Force | Out-Null
    if ($settingsExisted) {
        # Before .original existed, .overwatch-backup was written once and held the original.
        if (-not (Test-Path $originalBackupPath)) {
            Copy-Item $(if (Test-Path $backupPath) { $backupPath } else { $settingsPath }) $originalBackupPath
        }
        Copy-Item $settingsPath $backupPath -Force
    }
    Remove-OverwatchHooks $settings["hooks"]
    Add-OverwatchHooks $settings["hooks"]
    Write-FileAtomic $settingsPath ($settings | ConvertTo-Json -Depth 100)
} catch {
    Stop-WriteFailed $_.Exception.Message $configWritten $stoppedAgent
}

Write-Host "[+] Overwatch hooks merged into $settingsPath"
if ($settingsExisted) { Write-Host "[+] Settings before this run: $backupPath" }
if (Test-Path $originalBackupPath) { Write-Host "[+] Settings before Overwatch first changed them: $originalBackupPath" }
Write-Host "[+] Config saved to $configPath"
if ($mode -eq "pilot") {
    Write-Host "[+] Mode: pilot. Usage is recorded here and shipped to $($config["apiUrl"])"
} else {
    Write-Host "[+] Mode: local. Usage is recorded on this machine only"
    if ($droppedServer) { Write-Host "[+] Removed the API address and key, which local mode does not use" }
}
if ($restartNeeded -and $NoProcessControl) {
    Write-Host "[!] Agent not restarted: an $agentName that is already running keeps its old settings until it restarts"
}
if ($stoppedAgent) { Write-Host "[+] Stopped the running agent so it does not keep its old settings" }

if ($NoStart) {
    Write-Host "[+] -NoStart: did not register the scheduled task or start the agent"
    exit 0
}

# The agent is a standalone background process — nothing else ships queued events to the API.
# Register it as a per-user scheduled task so it auto-starts at login and survives reboots
# without anyone having to open a terminal. (The SessionStart hook also does a lazy-start check
# as a safety net in case this task isn't registered on a given machine.)
$agentCommand = Get-Command $agentName -ErrorAction SilentlyContinue
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
        Write-Host "    You can start the agent manually for now: $agentName"
    }

    if ($NoProcessControl) {
        Write-Host "[+] -NoProcessControl: did not start the agent"
    } elseif ((Get-OwnAgent).Count -eq 0) {
        Start-Process -FilePath $agentPath -WindowStyle Hidden
        Write-Host "[+] Started the agent now (runs hidden; check Task Manager > Details > overwatch-agent.exe)"
    } else {
        Write-Host "[+] Agent is already running with these settings"
    }
} else {
    Write-Host "[!] $agentName not found on PATH — install it first:"
    Write-Host "    dotnet tool install -g DeveloperOverwatch.Agent"
    Write-Host "    Then re-run this script to register auto-start."
}
