using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace DeveloperOverwatch.Agent.Services;

public sealed record CapturedTurn(
    string Role,
    string Content,
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
    /// plus the file's current total line count so the caller can advance its cursor.
    /// </summary>
    Task<ConversationCaptureResult> CaptureFromFileAsync(
        string transcriptPath, int fromLineIndex, CancellationToken ct);

    string? FindProjectDir(string repoPath);
}

public sealed class ConversationCapture : IConversationCapture
{
    private static readonly string ClaudeProjectsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        "projects");

    private readonly ILogger<ConversationCapture> _logger;

    private static readonly Regex TaskNotificationRegex = new(
        @"<task-notification>.*?<task-id>([^<]+)</task-id>.*?<subagent_tokens>(\d+)</subagent_tokens>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public ConversationCapture(ILogger<ConversationCapture> logger)
    {
        _logger = logger;
    }

    public async Task<ConversationCaptureResult> CaptureFromFileAsync(
        string transcriptPath, int fromLineIndex, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(transcriptPath))
            {
                _logger.LogDebug("Transcript file not found: {Path}", transcriptPath);
                return new ConversationCaptureResult([], fromLineIndex);
            }

            var turns = new List<CapturedTurn>();
            var lineIndex = 0;

            // Subagents (the "Agent" tool) resolve to their own model at launch time — a field
            // that never appears on the main conversation's own turns — and only report their
            // actual token usage later, in an unrelated "queue-operation" completion event
            // correlated by agentId. Both must be scanned from the start of the file regardless
            // of the cursor, since a long-running subagent's launch can be far behind
            // fromLineIndex by the time its completion is captured.
            var agentModelsById = new Dictionary<string, string>();

            using var stream = new FileStream(
                transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    TryTrackAgentLaunch(line, agentModelsById);

                    if (lineIndex >= fromLineIndex)
                    {
                        var turn = TryParseTurn(line);
                        if (turn is not null)
                            turns.Add(turn);

                        var subagentTurn = TryParseSubagentCompletion(line, agentModelsById);
                        if (subagentTurn is not null)
                            turns.Add(subagentTurn);
                    }
                }

                lineIndex++;
            }

            return new ConversationCaptureResult(turns, lineIndex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Conversation capture failed for {Path} — returning empty", transcriptPath);
            return new ConversationCaptureResult([], fromLineIndex);
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

    private CapturedTurn? TryParseTurn(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("message", out var message))
                return null;

            if (!message.TryGetProperty("role", out var roleProp))
                return null;

            var role = roleProp.GetString();
            if (string.IsNullOrEmpty(role))
                return null;

            var content = ExtractContent(message);
            if (string.IsNullOrEmpty(content))
                return null;

            DateTime? timestamp = null;
            if (root.TryGetProperty("timestamp", out var tsProp))
                timestamp = TryParseTimestamp(tsProp);

            int? inputTokens = null, outputTokens = null, cacheCreation = null, cacheRead = null;
            if (message.TryGetProperty("usage", out var usage))
            {
                inputTokens = TryGetInt(usage, "input_tokens");
                outputTokens = TryGetInt(usage, "output_tokens");
                cacheCreation = TryGetInt(usage, "cache_creation_input_tokens");
                cacheRead = TryGetInt(usage, "cache_read_input_tokens");
            }

            string? model = null;
            if (message.TryGetProperty("model", out var modelProp))
                model = modelProp.GetString();

            return new CapturedTurn(
                role, content, timestamp, inputTokens, outputTokens, cacheCreation, cacheRead, model);
        }
        catch (JsonException)
        {
            _logger.LogDebug("Skipping malformed JSONL line");
            return null;
        }
    }

    /// <summary>Records which model a subagent (the "Agent" tool) resolved to at launch time, so
    /// its later completion event — which carries token usage but no model name — can be
    /// attributed correctly.</summary>
    private static void TryTrackAgentLaunch(string line, Dictionary<string, string> agentModelsById)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("toolUseResult", out var toolUseResult))
                return;
            if (toolUseResult.ValueKind != JsonValueKind.Object)
                return;
            if (!toolUseResult.TryGetProperty("agentId", out var idProp) || idProp.ValueKind != JsonValueKind.String)
                return;
            if (!toolUseResult.TryGetProperty("resolvedModel", out var modelProp) || modelProp.ValueKind != JsonValueKind.String)
                return;

            var agentId = idProp.GetString();
            var model = modelProp.GetString();
            if (!string.IsNullOrEmpty(agentId) && !string.IsNullOrEmpty(model))
                agentModelsById[agentId] = model;
        }
        catch (JsonException)
        {
            // Not every line is JSON-parseable as an object with this shape — expected, not an error.
        }
    }

    /// <summary>A subagent's actual token usage only ever appears in a "queue-operation" entry
    /// bearing a `<task-notification>` string once it completes — surfaced here as a synthetic
    /// turn so it flows through the same cost-attribution and model-breakdown pipeline as every
    /// other turn, rather than needing its own separate tracking path.</summary>
    private static CapturedTurn? TryParseSubagentCompletion(string line, IReadOnlyDictionary<string, string> agentModelsById)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "queue-operation")
                return null;
            if (!root.TryGetProperty("content", out var contentProp) || contentProp.ValueKind != JsonValueKind.String)
                return null;

            var content = contentProp.GetString();
            if (string.IsNullOrEmpty(content))
                return null;

            var match = TaskNotificationRegex.Match(content);
            if (!match.Success)
                return null;

            var taskId = match.Groups[1].Value.Trim();
            if (!int.TryParse(match.Groups[2].Value, out var tokens))
                return null;

            agentModelsById.TryGetValue(taskId, out var model);

            DateTime? timestamp = root.TryGetProperty("timestamp", out var tsProp) ? TryParseTimestamp(tsProp) : null;

            // The notification reports one combined token count with no input/output split.
            // Treating it as output tokens is an approximation — subagent work is generally
            // generation-heavy (reports, analysis), so this prices closer to the true cost than
            // treating it as input tokens would.
            return new CapturedTurn(
                Role: "subagent",
                Content: $"Subagent completed ({tokens:N0} tokens)",
                Timestamp: timestamp,
                OutputTokens: tokens,
                Model: model);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? TryGetInt(JsonElement obj, string propertyName) =>
        obj.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : null;

    private static string? ExtractContent(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var contentProp))
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
            if (element.ValueKind != JsonValueKind.Object)
                continue;

            // Only surface actual conversational text — skip "thinking", "tool_use", and
            // "tool_result" blocks, which are captured separately via the PostToolUse hook.
            if (!element.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "text")
                continue;

            if (element.TryGetProperty("text", out var textProp))
            {
                var text = textProp.GetString();
                if (!string.IsNullOrEmpty(text))
                    parts.Add(text);
            }
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
}
