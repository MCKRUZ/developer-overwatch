using System.Text.Json;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>
/// Maps a Claude session id to an Overwatch session id the way the hooks do, through
/// <c>~/.overwatch/sessions/&lt;claude-session-id&gt;.json</c> (<c>{ ourSessionId, repoPath }</c>, written by
/// overwatch-session-start.ps1). A session the hooks never saw gets an id derived from the Claude id, so
/// every backfill run lands on the same Overwatch session.
/// </summary>
internal static class BackfillSessionIds
{
    public static async Task<BackfillSession> ResolveAsync(string overwatchDir, string claudeSessionId, CancellationToken ct)
    {
        var path = MappingPath(overwatchDir, claudeSessionId);
        if (File.Exists(path))
        {
            try
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("ourSessionId", out var id)
                    && id.ValueKind == JsonValueKind.String
                    && id.GetString() is { Length: > 0 } mapped)
                {
                    return new BackfillSession(mapped, Mapped: true);
                }
            }
            catch (JsonException)
            {
                // A corrupt mapping is treated as absent.
            }
        }

        // Namespaced so it can never equal a turn id hashed from a transcript source id.
        return new BackfillSession(StableTurnId.From($"overwatch:claude-session:{claudeSessionId}"), Mapped: false);
    }

    public static async Task WriteMappingAsync(
        string overwatchDir, string claudeSessionId, string sessionId, string? repoPath, CancellationToken ct)
    {
        var path = MappingPath(overwatchDir, claudeSessionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { ourSessionId = sessionId, repoPath }), ct);
    }

    private static string MappingPath(string overwatchDir, string claudeSessionId) =>
        Path.Combine(overwatchDir, "sessions", $"{claudeSessionId}.json");
}
