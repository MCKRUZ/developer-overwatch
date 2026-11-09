using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>
/// Adds what the session-start hook leaves out so the hook stays fast: which agent ran the
/// session, on which machine, and a normalized project key from the repository's git remote.
/// </summary>
internal static class SessionStartPayload
{
    public const string AgentName = "claude-code";
    private const int MaxMachineNameLength = 255;
    private const int MaxProjectKeyLength = 512;

    /// <summary>The session's project key, exactly as the server receives it. Kept locally too,
    /// so usage recorded on the machine groups by the same project.</summary>
    /// <param name="rawPayload">The payload as queued — the repository path is read from here,
    /// since redaction may rewrite strings.</param>
    public static async Task<string?> ResolveProjectKeyAsync(
        string rawPayload, IGitReader gitRemote, CancellationToken ct)
    {
        var repoPath = ReadRepoPath(rawPayload);
        var remoteUrl = repoPath is null ? null : await gitRemote.GetOriginUrlAsync(repoPath, ct);
        return Truncate(ProjectKey.From(remoteUrl, repoPath), MaxProjectKeyLength);
    }

    /// <param name="redactedPayload">The payload that will be sent; returned unchanged when it is
    /// not a JSON object, because a session start must never be dropped.</param>
    /// <param name="projectKey">From <see cref="ResolveProjectKeyAsync"/>.</param>
    public static string Enrich(string redactedPayload, string? projectKey)
    {
        JsonObject? body;
        try
        {
            body = JsonNode.Parse(redactedPayload) as JsonObject;
        }
        catch (JsonException)
        {
            return redactedPayload;
        }

        if (body is null)
            return redactedPayload;

        body["agent"] = AgentName;
        body["machineName"] = Truncate(Environment.MachineName, MaxMachineNameLength);
        body["projectKey"] = projectKey;
        return body.ToJsonString();
    }

    private static string? ReadRepoPath(string rawPayload)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawPayload);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("repoPath", out var prop)
                && prop.ValueKind == JsonValueKind.String
                && prop.GetString() is { Length: > 0 } path
                    ? path
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is not null && value.Length > max ? value[..max] : value;
}
