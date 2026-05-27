using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DeveloperOverwatch.Agent.Services;

public sealed record CapturedTurn(string Role, string Content, DateTime? Timestamp);

public interface IConversationCapture
{
    Task<IReadOnlyList<CapturedTurn>> CaptureAsync(string? repoPath, CancellationToken ct);
    string? FindProjectDir(string repoPath);
}

public sealed class ConversationCapture : IConversationCapture
{
    private static readonly string ClaudeProjectsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        "projects");

    private readonly ILogger<ConversationCapture> _logger;

    public ConversationCapture(ILogger<ConversationCapture> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<CapturedTurn>> CaptureAsync(string? repoPath, CancellationToken ct)
    {
        try
        {
            var searchDir = ResolveSearchDirectory(repoPath);
            if (searchDir is null)
            {
                _logger.LogDebug("No Claude projects directory found");
                return [];
            }

            var conversationFile = FindMostRecentConversation(searchDir);
            if (conversationFile is null)
            {
                _logger.LogDebug("No conversation files found in {Dir}", searchDir);
                return [];
            }

            return await ParseConversationFileAsync(conversationFile, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Conversation capture failed — returning empty");
            return [];
        }
    }

    public string? FindProjectDir(string repoPath)
    {
        try
        {
            if (!Directory.Exists(ClaudeProjectsDir))
                return null;

            var normalized = NormalizePath(repoPath);

            foreach (var dir in Directory.EnumerateDirectories(ClaudeProjectsDir))
            {
                if (MatchesProjectDir(dir, normalized))
                    return dir;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to find project dir for {Path}", repoPath);
            return null;
        }
    }

    private string? ResolveSearchDirectory(string? repoPath)
    {
        if (!Directory.Exists(ClaudeProjectsDir))
            return null;

        if (repoPath is not null)
        {
            var projectDir = FindProjectDir(repoPath);
            if (projectDir is not null)
                return projectDir;
        }

        return ClaudeProjectsDir;
    }

    private static FileInfo? FindMostRecentConversation(string directory)
    {
        var dir = new DirectoryInfo(directory);
        if (!dir.Exists)
            return null;

        return dir.EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
    }

    private async Task<IReadOnlyList<CapturedTurn>> ParseConversationFileAsync(
        FileInfo file, CancellationToken ct)
    {
        var turns = new List<CapturedTurn>();

        using var reader = new StreamReader(file.FullName);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var turn = TryParseTurn(line);
            if (turn is not null)
                turns.Add(turn);
        }

        _logger.LogDebug("Parsed {Count} turns from {File}", turns.Count, file.Name);
        return turns;
    }

    private CapturedTurn? TryParseTurn(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("role", out var roleProp))
                return null;

            var role = roleProp.GetString();
            if (string.IsNullOrEmpty(role))
                return null;

            var content = ExtractContent(root);
            if (string.IsNullOrEmpty(content))
                return null;

            DateTime? timestamp = null;
            if (root.TryGetProperty("timestamp", out var tsProp))
                timestamp = TryParseTimestamp(tsProp);

            return new CapturedTurn(role, content, timestamp);
        }
        catch (JsonException)
        {
            _logger.LogDebug("Skipping malformed JSONL line");
            return null;
        }
    }

    private static string? ExtractContent(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var contentProp))
            return null;

        return contentProp.ValueKind switch
        {
            JsonValueKind.String => contentProp.GetString(),
            JsonValueKind.Array => ConcatenateContentParts(contentProp),
            _ => null
        };
    }

    private static string? ConcatenateContentParts(JsonElement array)
    {
        var parts = new List<string>();

        foreach (var element in array.EnumerateArray())
        {
            var text = element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Object when element.TryGetProperty("text", out var textProp)
                    => textProp.GetString(),
                _ => null
            };

            if (!string.IsNullOrEmpty(text))
                parts.Add(text);
        }

        return parts.Count > 0 ? string.Join("\n", parts) : null;
    }

    private static DateTime? TryParseTimestamp(JsonElement prop)
    {
        try
        {
            return prop.ValueKind switch
            {
                JsonValueKind.String when DateTime.TryParse(prop.GetString(), out var dt) => dt,
                JsonValueKind.Number => DateTimeOffset.FromUnixTimeMilliseconds(prop.GetInt64()).UtcDateTime,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool MatchesProjectDir(string dir, string normalizedRepoPath)
    {
        // Claude Code project dirs use a hash of the repo path as the directory name.
        // Check for a config/metadata file inside that references the repo path.
        try
        {
            var configCandidates = new[] { "config.json", ".project", "project.json" };

            foreach (var candidate in configCandidates)
            {
                var configPath = Path.Combine(dir, candidate);
                if (!File.Exists(configPath))
                    continue;

                var configContent = File.ReadAllText(configPath);
                if (configContent.Contains(normalizedRepoPath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // Fallback: directory name itself might encode the path (dash-separated segments)
            var dirName = Path.GetFileName(dir);
            if (dirName is not null)
            {
                var pathSegments = normalizedRepoPath.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);

                if (pathSegments.Length >= 2)
                {
                    var lastTwo = string.Join("-", pathSegments[^2..]);
                    if (dirName.Contains(lastTwo, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch
        {
            // Best effort — if we can't read configs, this dir is a miss
        }

        return false;
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
