using System.ComponentModel;
using System.Diagnostics;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>Reads from git: a repository's origin remote and config values.</summary>
public interface IGitReader
{
    /// <summary>The <c>origin</c> remote URL of the repository at <paramref name="repoPath"/>, or
    /// null when there is none, git is missing, or git does not answer in time.</summary>
    Task<string?> GetOriginUrlAsync(string repoPath, CancellationToken ct);

    /// <summary>A git config value such as <c>user.email</c>, as git resolves it for the folder at
    /// <paramref name="repoPath"/>; the global value when that folder is null or gone. Null when unset,
    /// git is missing, or git does not answer in time.</summary>
    Task<string?> GetConfigValueAsync(string? repoPath, string key, CancellationToken ct);
}

public sealed class GitReader : IGitReader
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public Task<string?> GetOriginUrlAsync(string repoPath, CancellationToken ct) =>
        RunGitAsync(new[] { "-C", repoPath, "remote", "get-url", "origin" }, ct);

    public Task<string?> GetConfigValueAsync(string? repoPath, string key, CancellationToken ct) =>
        RunGitAsync(
            repoPath is not null && Directory.Exists(repoPath)
                ? new[] { "-C", repoPath, "config", key }
                : new[] { "config", key },
            ct);

    private static async Task<string?> RunGitAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process is null)
                return null;

            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var value = (await stdout).Trim();
            await stderr;

            return process.ExitCode == 0 && value.Length > 0 ? value : null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
        finally
        {
            if (process is { HasExited: false })
                process.Kill(entireProcessTree: true);
            process?.Dispose();
        }
    }
}
