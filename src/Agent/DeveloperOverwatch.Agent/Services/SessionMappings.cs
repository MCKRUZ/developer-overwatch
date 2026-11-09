using System.Text.Json;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>Reads the per-Claude-session mapping files under <c>~/.overwatch/sessions/</c>, written by
/// overwatch-session-start.ps1 as <c>{ ourSessionId, repoPath }</c>.</summary>
internal static class SessionMappings
{
    /// <summary>The repository path recorded for an Overwatch session id, or null. There is no direct
    /// lookup, since the files are keyed by Claude Code's own session id, not ours.</summary>
    public static async Task<string?> FindRepoPathAsync(string overwatchDir, string sessionId)
    {
        try
        {
            var sessionsDir = Path.Combine(overwatchDir, "sessions");
            if (!Directory.Exists(sessionsDir))
                return null;

            foreach (var file in Directory.EnumerateFiles(sessionsDir, "*.json"))
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file));
                if (doc.RootElement.TryGetProperty("ourSessionId", out var idProp) &&
                    idProp.GetString() == sessionId &&
                    doc.RootElement.TryGetProperty("repoPath", out var repoProp))
                {
                    return repoProp.GetString();
                }
            }
        }
        catch
        {
            // best effort
        }
        return null;
    }
}
