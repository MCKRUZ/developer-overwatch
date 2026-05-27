using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using DeveloperOverwatch.Agent.Models;

namespace DeveloperOverwatch.Agent.Services;

public interface IGitFallbackWriter
{
    Task<bool> WriteFallbackAsync(string sessionId, IReadOnlyList<QueuedEvent> events, string? repoPath, CancellationToken ct);
    Task<bool> AddGitTrailerAsync(string sessionId, string repoPath, CancellationToken ct);
}

public sealed partial class GitFallbackWriter : IGitFallbackWriter
{
    private readonly ILogger<GitFallbackWriter> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [GeneratedRegex(@"^[a-zA-Z0-9\-_]+$")]
    private static partial Regex SafeSessionIdPattern();

    public GitFallbackWriter(ILogger<GitFallbackWriter> logger)
    {
        _logger = logger;
    }

    public async Task<bool> WriteFallbackAsync(string sessionId, IReadOnlyList<QueuedEvent> events, string? repoPath, CancellationToken ct)
    {
        try
        {
            if (!TryResolveRepoPath(repoPath, out var canonicalRepoPath))
                return false;

            if (!SafeSessionIdPattern().IsMatch(sessionId))
            {
                _logger.LogWarning("Session ID contains unsafe characters, rejecting: {SessionId}", sessionId);
                return false;
            }

            var sessionsDir = Path.Combine(canonicalRepoPath, ".sessions");
            Directory.CreateDirectory(sessionsDir);

            var filePath = Path.Combine(sessionsDir, $"{sessionId}.json");
            var canonicalFilePath = Path.GetFullPath(filePath);

            // Verify the resolved file path is still inside .sessions/
            if (!canonicalFilePath.StartsWith(Path.GetFullPath(sessionsDir), StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Resolved file path escapes sessions directory: {FilePath}", canonicalFilePath);
                return false;
            }

            var payload = new FallbackPayload
            {
                SessionId = sessionId,
                Events = events.Select(e => new FallbackEvent
                {
                    Id = e.Id,
                    EventType = e.EventType.ToString(),
                    PayloadJson = e.PayloadJson,
                    CreatedAt = e.CreatedAt
                }).ToArray()
            };

            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            await File.WriteAllTextAsync(canonicalFilePath, json, ct);

            _logger.LogInformation("Wrote fallback for session {SessionId}: {EventCount} events to {Path}",
                sessionId, events.Count, canonicalFilePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write fallback for session {SessionId}", sessionId);
            return false;
        }
    }

    public async Task<bool> AddGitTrailerAsync(string sessionId, string repoPath, CancellationToken ct)
    {
        try
        {
            if (!TryResolveRepoPath(repoPath, out var canonicalRepoPath))
                return false;

            if (!SafeSessionIdPattern().IsMatch(sessionId))
            {
                _logger.LogWarning("Session ID contains unsafe characters, rejecting: {SessionId}", sessionId);
                return false;
            }

            var commitHash = await RunGitAsync(canonicalRepoPath, "log -1 --format=%H", ct);
            if (string.IsNullOrWhiteSpace(commitHash))
            {
                _logger.LogDebug("No recent commit found in {RepoPath}", canonicalRepoPath);
                return false;
            }

            var result = await RunGitAsync(canonicalRepoPath, $"notes append -m \"Claude-Session: {sessionId}\" HEAD", ct);
            if (result is null)
                return false;

            _logger.LogInformation("Added git note for session {SessionId} on commit {Commit}",
                sessionId, commitHash.Trim());
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add git trailer for session {SessionId}", sessionId);
            return false;
        }
    }

    private bool TryResolveRepoPath(string? repoPath, out string canonicalPath)
    {
        canonicalPath = string.Empty;

        if (string.IsNullOrWhiteSpace(repoPath))
        {
            _logger.LogDebug("Repo path is null or empty, skipping");
            return false;
        }

        canonicalPath = Path.GetFullPath(repoPath);

        if (!Directory.Exists(canonicalPath))
        {
            _logger.LogDebug("Repo path does not exist: {RepoPath}", canonicalPath);
            return false;
        }

        return true;
    }

    private async Task<string?> RunGitAsync(string repoPath, string arguments, CancellationToken ct)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"-C \"{repoPath}\" {arguments}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            var error = await process.StandardError.ReadToEndAsync(ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(timeoutCts.Token);

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("git {Arguments} failed (exit {ExitCode}): {Error}",
                    arguments, process.ExitCode, error.Trim());
                return null;
            }

            return output;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "git command failed: git -C {RepoPath} {Arguments}", repoPath, arguments);
            return null;
        }
    }

    private sealed class FallbackPayload
    {
        public required string SessionId { get; init; }
        public required FallbackEvent[] Events { get; init; }
    }

    private sealed class FallbackEvent
    {
        public required string Id { get; init; }
        public required string EventType { get; init; }
        public required string PayloadJson { get; init; }
        public DateTime CreatedAt { get; init; }
    }
}
