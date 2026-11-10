using System.ComponentModel;
using System.Diagnostics;
using FluentAssertions;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>Runs a script from the repository's hooks folder under pwsh, with USERPROFILE pointed at a
/// temp folder so the real ~/.claude and ~/.overwatch are never touched.</summary>
internal static class Pwsh
{
    public static readonly string HooksDir = FindHooksDir();

    /// <summary>Starts the script. A hook then waits for its stdin to close. Null when pwsh is not installed.</summary>
    public static Process? Start(string script, string home, params string[] args)
    {
        var psi = new ProcessStartInfo("pwsh")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(HooksDir, script) }.Concat(args))
            psi.ArgumentList.Add(arg);
        psi.Environment["USERPROFILE"] = home;

        try
        {
            return Process.Start(psi);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Writes <paramref name="stdin"/> and closes it, or leaves stdin alone when null because the
    /// caller already did, then waits for the script to exit.</summary>
    public static PwshResult Finish(Process process, string? stdin)
    {
        using (process)
        {
            if (stdin is not null)
            {
                process.StandardInput.Write(stdin);
                process.StandardInput.Close();
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(60_000).Should().BeTrue("the scripts finish in seconds");
            return new PwshResult(process.ExitCode, stdout, stderr);
        }
    }

    private static string FindHooksDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var hooks = Path.Combine(dir.FullName, "hooks");
            if (File.Exists(Path.Combine(hooks, "overwatch-stop.ps1")))
                return hooks;
        }
        throw new InvalidOperationException("hooks directory not found above the test output");
    }
}

internal sealed record PwshResult(int ExitCode, string Stdout, string Stderr);
