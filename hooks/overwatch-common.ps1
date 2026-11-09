# hooks/overwatch-common.ps1
# Dot-sourced by every Overwatch hook. Not a hook itself, so the installer never registers it.
# Defines functions only: it must never write to stdout, which Claude Code parses.

# Appends one line to the fallback file the agent imports.
#
# Async hooks can run at the same moment. The file is opened exclusively (FileShare.None, which
# .NET also maps to an advisory lock on Linux and macOS), so a second hook waits and retries
# instead of losing its event. Shared-write append is not safe here: since .NET 6 each process
# keeps its own file offset, so two appenders opening at the same end-of-file write over each other.
function Add-OverwatchLine([string]$Path, [string]$Line) {
    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($Line + [Environment]::NewLine)
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        try {
            $stream = [System.IO.File]::Open(
                $Path, [System.IO.FileMode]::Append, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
            try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
            return
        } catch {
            Start-Sleep -Milliseconds (Get-Random -Minimum 25 -Maximum 51)
        }
    }
}
