# hooks/install-hooks.ps1
# Installs Overwatch hooks into Claude Code settings and configures the local agent
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

if (-not $settings.hooks) { $settings | Add-Member -NotePropertyName hooks -NotePropertyValue @{} }

$settings.hooks.SessionStart = @(@{
    type = "command"
    command = "pwsh -NoProfile -File `"$hooksDir\overwatch-session-start.ps1`""
})

$settings.hooks.PostToolUse = @(@{
    type = "command"
    command = "pwsh -NoProfile -File `"$hooksDir\overwatch-post-tool-use.ps1`""
})

$settings.hooks.Stop = @(@{
    type = "command"
    command = "pwsh -NoProfile -File `"$hooksDir\overwatch-session-end.ps1`""
})

$settings | ConvertTo-Json -Depth 10 | Set-Content $settingsPath

Write-Host "[+] Overwatch hooks installed to $settingsPath"
Write-Host "[+] Config saved to $(Join-Path $overwatchDir 'config.json')"
Write-Host "[+] API URL: $ApiUrl"
Write-Host ""
Write-Host "Hooks now write to local SQLite queue at $(Join-Path $overwatchDir 'queue.db')"
Write-Host "Start the agent to ship events: overwatch-agent"
Write-Host "  Or install globally: dotnet tool install -g DeveloperOverwatch.Agent"
