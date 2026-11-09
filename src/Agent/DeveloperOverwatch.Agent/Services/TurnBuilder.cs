using System.Text.Json;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>
/// Turns transcript lines into conversation turns. Claude Code writes one API response as several
/// lines — one per content block (thinking, text, tool_use) — and repeats the same message id and
/// usage on each. So responses are grouped by message id and their usage is counted once, whether
/// or not the response contains any text.
/// </summary>
internal sealed class TurnBuilder
{
    private readonly bool _asSubagent;
    private readonly List<PendingTurn> _turns = [];
    private readonly Dictionary<string, PendingTurn> _responsesBySourceId = new(StringComparer.Ordinal);

    public TurnBuilder(bool asSubagent)
    {
        _asSubagent = asSubagent;
    }

    /// <summary>Adds one transcript line. Returns false only when the line is not valid JSON.</summary>
    public bool TryAdd(string line, string fallbackSourceId)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !IsConversationLine(root)
                || !root.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object
                || GetString(message, "role") is not { Length: > 0 } role)
            {
                return true;
            }

            var timestamp = root.TryGetProperty("timestamp", out var tsProp) ? ParseTimestamp(tsProp) : null;

            if (role == "assistant")
                AddResponseLine(message, timestamp, fallbackSourceId);
            else if (!_asSubagent)
                AddPrompt(root, message, role, timestamp, fallbackSourceId);

            return true;
        }
    }

    public IReadOnlyList<CapturedTurn> Build() =>
        _turns.Select(t => new CapturedTurn(
            t.SourceId,
            t.Role,
            t.Texts.Count > 0 ? string.Join("\n", t.Texts) : null,
            t.Timestamp,
            t.InputTokens,
            t.OutputTokens,
            t.CacheCreationInputTokens,
            t.CacheReadInputTokens,
            t.Model)).ToList();

    private void AddResponseLine(JsonElement message, DateTime? timestamp, string fallbackSourceId)
    {
        var sourceId = GetString(message, "id") is { Length: > 0 } messageId ? $"msg:{messageId}" : fallbackSourceId;

        if (!_responsesBySourceId.TryGetValue(sourceId, out var turn))
        {
            turn = new PendingTurn(sourceId, _asSubagent ? "subagent" : "assistant", timestamp);
            _responsesBySourceId[sourceId] = turn;
            _turns.Add(turn);
        }

        turn.Texts.AddRange(ExtractTexts(message));
        turn.Model ??= GetString(message, "model");

        if (message.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            // Today every line of a response repeats the final usage; older builds wrote partial
            // output counts on early lines. Keeping the largest value per field is right for both.
            turn.InputTokens = Larger(turn.InputTokens, GetInt(usage, "input_tokens"));
            turn.OutputTokens = Larger(turn.OutputTokens, GetInt(usage, "output_tokens"));
            turn.CacheCreationInputTokens = Larger(turn.CacheCreationInputTokens, GetInt(usage, "cache_creation_input_tokens"));
            turn.CacheReadInputTokens = Larger(turn.CacheReadInputTokens, GetInt(usage, "cache_read_input_tokens"));
        }
    }

    private void AddPrompt(JsonElement root, JsonElement message, string role, DateTime? timestamp, string fallbackSourceId)
    {
        // Tool results arrive as "user" lines with no text block; the PostToolUse hook already
        // captures tool activity, so only real prompts become turns.
        var texts = ExtractTexts(message);
        if (texts.Count == 0)
            return;

        var sourceId = GetString(root, "uuid") is { Length: > 0 } uuid ? $"uuid:{uuid}" : fallbackSourceId;
        var turn = new PendingTurn(sourceId, role, timestamp);
        turn.Texts.AddRange(texts);
        _turns.Add(turn);
    }

    // Other line types also embed a "message" — notably "api-request-blob", a copy of each
    // outgoing request that repeats earlier messages. Only real user/assistant lines are turns.
    private static bool IsConversationLine(JsonElement root) =>
        GetString(root, "type") is null or "user" or "assistant";

    private static List<string> ExtractTexts(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
            return [];

        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() is { Length: > 0 } text ? [text] : [];

        if (content.ValueKind != JsonValueKind.Array)
            return [];

        // Only conversational text — "thinking", "tool_use" and "tool_result" blocks are skipped.
        return content.EnumerateArray()
            .Where(block => block.ValueKind == JsonValueKind.Object && GetString(block, "type") == "text")
            .Select(block => GetString(block, "text"))
            .OfType<string>()
            .Where(text => text.Length > 0)
            .ToList();
    }

    private static int? Larger(int? current, int? candidate) =>
        current is null ? candidate : candidate is null ? current : Math.Max(current.Value, candidate.Value);

    private static string? GetString(JsonElement obj, string propertyName) =>
        obj.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static int? GetInt(JsonElement obj, string propertyName) =>
        obj.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetInt32(out var value)
            ? value
            : null;

    private static DateTime? ParseTimestamp(JsonElement prop) => prop.ValueKind switch
    {
        JsonValueKind.String when DateTime.TryParse(prop.GetString(), out var dt) => dt,
        JsonValueKind.Number when prop.TryGetInt64(out var ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime,
        _ => null
    };

    private sealed class PendingTurn(string sourceId, string role, DateTime? timestamp)
    {
        public string SourceId { get; } = sourceId;
        public string Role { get; } = role;
        public DateTime? Timestamp { get; } = timestamp;
        public List<string> Texts { get; } = [];
        public string? Model { get; set; }
        public int? InputTokens { get; set; }
        public int? OutputTokens { get; set; }
        public int? CacheCreationInputTokens { get; set; }
        public int? CacheReadInputTokens { get; set; }
    }
}
