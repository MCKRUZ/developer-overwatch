using Microsoft.Extensions.Logging;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>One conversation turn read from a transcript.</summary>
/// <param name="SourceId">Globally stable id for what this turn was read from — the API message
/// id for a model response, the line uuid for a user prompt. The same transcript content always
/// yields the same SourceId, including when a resumed or forked session copies earlier lines into
/// a new transcript, so the server can recognise a resend and count it once.</param>
/// <param name="Content">Conversational text, or null for a response that only called tools or
/// only thought — those still carry token usage and must be counted.</param>
public sealed record CapturedTurn(
    string SourceId,
    string Role,
    string? Content,
    DateTime? Timestamp,
    int? InputTokens = null,
    int? OutputTokens = null,
    int? CacheCreationInputTokens = null,
    int? CacheReadInputTokens = null,
    string? Model = null);

public sealed record ConversationCaptureResult(IReadOnlyList<CapturedTurn> Turns, int TotalLines);

public interface IConversationCapture
{
    /// <summary>
    /// Parses a Claude Code transcript JSONL file, returning only turns found after
    /// <paramref name="fromLineIndex"/> (0-based line count already processed by the caller),
    /// plus the line count the caller should store as its next cursor.
    /// </summary>
    /// <param name="asSubagent">True for a subagent's own transcript: only model responses are
    /// returned, with role "subagent", since the prompt and tool results belong to the parent.</param>
    Task<ConversationCaptureResult> CaptureFromFileAsync(
        string transcriptPath, int fromLineIndex, bool asSubagent = false, CancellationToken ct = default);

    /// <summary>Paths of the subagent transcripts Claude Code writes beside a session transcript
    /// (<c>&lt;session-id&gt;/subagents/agent-*.jsonl</c>), in a stable order.</summary>
    IReadOnlyList<string> FindSubagentTranscripts(string transcriptPath);

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

    public async Task<ConversationCaptureResult> CaptureFromFileAsync(
        string transcriptPath, int fromLineIndex, bool asSubagent = false, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(transcriptPath))
            {
                _logger.LogDebug("Transcript file not found: {Path}", transcriptPath);
                return new ConversationCaptureResult([], fromLineIndex);
            }

            var fileName = Path.GetFileNameWithoutExtension(transcriptPath);
            var builder = new TurnBuilder(asSubagent);
            var lineIndex = 0;
            var lastLineFailedToParse = false;

            using var stream = new FileStream(
                transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (lineIndex >= fromLineIndex && !string.IsNullOrWhiteSpace(line))
                    lastLineFailedToParse = !builder.TryAdd(line, $"{fileName}:line:{lineIndex}");

                lineIndex++;
            }

            // Claude Code may still be writing the transcript. A final line that does not parse
            // is most likely half-written, so leave the cursor on it to re-read it next time.
            var nextCursor = lastLineFailedToParse ? lineIndex - 1 : lineIndex;
            return new ConversationCaptureResult(builder.Build(), Math.Max(nextCursor, fromLineIndex));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Conversation capture failed for {Path} — returning empty", transcriptPath);
            return new ConversationCaptureResult([], fromLineIndex);
        }
    }

    public IReadOnlyList<string> FindSubagentTranscripts(string transcriptPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(transcriptPath);
            if (dir is null)
                return [];

            var subagentsDir = Path.Combine(dir, Path.GetFileNameWithoutExtension(transcriptPath), "subagents");
            if (!Directory.Exists(subagentsDir))
                return [];

            return Directory.GetFiles(subagentsDir, "*.jsonl")
                .Order(StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list subagent transcripts for {Path}", transcriptPath);
            return [];
        }
    }

    /// <summary>
    /// Best-effort fallback for locating a project's transcript directory when no explicit
    /// transcript_path was supplied. Claude Code names project directories by taking the
    /// absolute repo path and replacing ':', '\', '/', and '.' with '-' (e.g.
    /// "C:\Users\me\repo" -> "C--Users-me-repo").
    /// </summary>
    public string? FindProjectDir(string repoPath)
    {
        try
        {
            if (!Directory.Exists(ClaudeProjectsDir))
                return null;

            var encoded = EncodeProjectDirName(repoPath);
            var candidate = Path.Combine(ClaudeProjectsDir, encoded);
            return Directory.Exists(candidate) ? candidate : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to find project dir for {Path}", repoPath);
            return null;
        }
    }

    private static string EncodeProjectDirName(string repoPath)
    {
        var full = Path.GetFullPath(repoPath).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var chars = full.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is ':' or '\\' or '/' or '.')
                chars[i] = '-';
        }
        return new string(chars);
    }
}
