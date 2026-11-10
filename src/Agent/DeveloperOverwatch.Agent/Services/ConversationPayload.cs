using System.Globalization;
using System.Text.Json;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>What a ConversationTurn marker asks for. The Stop hook's marker names only the transcript.
/// Backfill's marker adds <c>usageOnly</c>, <c>isBackfill</c> and <c>from</c>: ship usage since <c>from</c>,
/// never text.</summary>
/// <param name="Backfill">The explicit backfill opt-in: its turns ship even from before enrolment. It always
/// means usage only, whatever <c>usageOnly</c> says.</param>
internal sealed record ConversationMarker(string TranscriptPath, bool UsageOnly, DateTime? From, bool Backfill = false)
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

            var backfill = IsTrue(root, "isBackfill");
            DateTime? from = root.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.String
                && DateTime.TryParse(f.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                    ? parsed.ToUniversalTime()
                    : null;
            return new ConversationMarker(path, backfill || IsTrue(root, "usageOnly"), from, backfill);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsTrue(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

/// <summary>Builds the <c>/events</c> payload that carries captured conversation turns.</summary>
internal static class ConversationPayload
{
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
                content = usageOnly ? null : FieldLimits.Clamp(t.Content, FieldLimits.TurnContent),
                tokenCount = TotalTokens(t) is var total && total > 0 ? total : (int?)null,
                inputTokens = t.InputTokens,
                outputTokens = t.OutputTokens,
                cacheCreationInputTokens = t.CacheCreationInputTokens,
                cacheReadInputTokens = t.CacheReadInputTokens,
                model = t.Model,
                // The server rejects longer names, and the turn would be dropped.
                gitBranch = FieldLimits.Clamp(t.GitBranch, FieldLimits.Name),
                subagentName = FieldLimits.Clamp(t.SubagentName, FieldLimits.Name),
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
    /// <param name="keptBefore">A title file last written before it names a session from before enrolment,
    /// and stays on this machine.</param>
    public static async Task<string?> ResolveSessionTitleAsync(string transcriptPath, DateTime? keptBefore, CancellationToken ct)
    {
        try
        {
            var dir = Path.GetDirectoryName(transcriptPath);
            if (dir is null)
                return null;

            var sessionFolder = Path.GetFileNameWithoutExtension(transcriptPath);
            var titlePath = Path.Combine(dir, sessionFolder, "custom-title.json");
            if (!File.Exists(titlePath) || (keptBefore is { } cutoff && File.GetLastWriteTimeUtc(titlePath) < cutoff))
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

    // Cache reads and writes are tokens the model processed too — usually most of them.
    private static int TotalTokens(CapturedTurn t) =>
        (t.InputTokens ?? 0) + (t.OutputTokens ?? 0) + (t.CacheCreationInputTokens ?? 0) + (t.CacheReadInputTokens ?? 0);
}
