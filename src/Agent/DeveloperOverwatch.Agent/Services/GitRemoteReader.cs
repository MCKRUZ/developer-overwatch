using System.ComponentModel;
using System.Diagnostics;

namespace DeveloperOverwatch.Agent.Services;

public interface IGitRemoteReader
{
    /// <summary>The <c>origin</c> remote URL of the repository at <paramref name="repoPath"/>, or
    /// null when there is none, git is missing, or git does not answer in time.</summary>
    Task<string?> GetOriginUrlAsync(string repoPath, CancellationToken ct);
}

public sealed class GitRemoteReader : IGitRemoteReader
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public async Task<string?> GetOriginUrlAsync(string repoPath, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in new[] { "-C", repoPath, "remote", "get-url", "origin" })
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
            var url = (await stdout).Trim();
            await stderr;

            return process.ExitCode == 0 && url.Length > 0 ? url : null;
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
