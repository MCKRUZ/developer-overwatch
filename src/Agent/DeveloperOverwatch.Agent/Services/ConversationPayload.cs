using System.Globalization;
using System.Text.Json;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>What a ConversationTurn marker asks for. The Stop hook's marker names only the transcript.
/// Backfill's marker adds <c>usageOnly</c> and <c>from</c>: ship usage since <c>from</c>, never text.</summary>
internal sealed record ConversationMarker(string TranscriptPath, bool UsageOnly, DateTime? From)
{
    /// <summary>Null when the payload is malformed or names no transcript.</summary>
    public static ConversationMarker? Parse(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("transcriptPath", out var tp) || tp.ValueKind != JsonValueKind.String
                || tp.GetString() is not { Length: > 0 } path)
                return null;

            var usageOnly = root.TryGetProperty("usageOnly", out var u) && u.ValueKind == JsonValueKind.True;
            DateTime? from = root.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.String
                && DateTime.TryParse(f.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                    ? parsed.ToUniversalTime()
                    : null;
            return new ConversationMarker(path, usageOnly, from);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Builds the <c>/events</c> payload that carries captured conversation turns.</summary>
internal static class ConversationPayload
{
    private const int MaxTurnContentLength = 4096;

    // The server rejects longer values, and a rejected batch would be retried forever.
    private const int MaxLabelLength = 255;

    /// <param name="usageOnly">Ship no conversation text: content is null and no secret is flagged.</param>
    public static string Build(
        string sessionId, string? sessionTitle, IReadOnlyList<CapturedTurn> turns, IPrivacyRedactor redactor, bool usageOnly) =>
        JsonSerializer.Serialize(new
        {
            sessionId,
            sessionTitle,
            conversationTurns = turns.Select((t, i) => new
            {
                id = StableTurnId.From(t.SourceId),
                turnIndex = i,
                role = t.Role,
                content = usageOnly ? null : Clamp(t.Content, MaxTurnContentLength),
                tokenCount = TotalTokens(t) is var total && total > 0 ? total : (int?)null,
                inputTokens = t.InputTokens,
                outputTokens = t.OutputTokens,
                cacheCreationInputTokens = t.CacheCreationInputTokens,
                cacheReadInputTokens = t.CacheReadInputTokens,
                model = t.Model,
                gitBranch = Clamp(t.GitBranch, MaxLabelLength),
                subagentName = Clamp(t.SubagentName, MaxLabelLength),
                // Computed from the RAW (pre-redaction) content — by the time this payload is
                // redacted, the secret itself is gone, so this flag is the only signal the server
                // ever gets that one was present.
                containsRedactedSecret = !usageOnly && t.Content is not null && redactor.ContainsSensitive(t.Content),
                timestamp = (t.Timestamp ?? DateTime.UtcNow).ToString("o"),
                isDecision = false
            }),
            toolExecutions = Array.Empty<object>(),
            fileChanges = Array.Empty<object>()
        });

    // Claude Code stores a `/rename`d session title in a sidecar file next to the transcript
    // (<projects-dir>/<encoded-repo>/<session-id>/custom-title.json), not in the transcript
    // itself — read it fresh on every capture so a mid-session rename shows up on the dashboard
    // instead of the raw session GUID.
    public static async Task<string?> ResolveSessionTitleAsync(string transcriptPath, CancellationToken ct)
    {
        try
        {
            var dir = Path.GetDirectoryName(transcriptPath);
            if (dir is null)
                return null;

            var sessionFolder = Path.GetFileNameWithoutExtension(transcriptPath);
            var titlePath = Path.Combine(dir, sessionFolder, "custom-title.json");
            if (!File.Exists(titlePath))
                return null;

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(titlePath, ct));
            return doc.RootElement.TryGetProperty("customTitle", out var titleProp)
                ? titleProp.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Clamp(string? value, int max) =>
        value is { Length: var length } && length > max ? value[..max] : value;

    // Cache reads and writes are tokens the model processed too — usually most of them.
    private static int TotalTokens(CapturedTurn t) =>
        (t.InputTokens ?? 0) + (t.OutputTokens ?? 0) + (t.CacheCreationInputTokens ?? 0) + (t.CacheReadInputTokens ?? 0);
}
