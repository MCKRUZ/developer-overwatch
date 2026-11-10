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

    /// <summary>The session's project key, exactly as the server receives it. Kept locally too,
    /// so usage recorded on the machine groups by the same project.</summary>
    /// <param name="rawPayload">The payload as queued — the repository path is read from here,
    /// since redaction may rewrite strings.</param>
    public static async Task<string?> ResolveProjectKeyAsync(
        string rawPayload, IGitReader gitRemote, CancellationToken ct)
    {
        var repoPath = ReadRepoPath(rawPayload);
        var remoteUrl = repoPath is null ? null : await gitRemote.GetOriginUrlAsync(repoPath, ct);
        return FieldLimits.Clamp(ProjectKey.From(remoteUrl, repoPath), FieldLimits.ProjectKey);
    }

    /// <summary>Also normalizes what the server would otherwise reject outright: an invalid email
    /// becomes <see cref="FieldLimits.UnknownEmail"/>. Runs on the redacted payload, since redaction
    /// may rewrite the email.</summary>
    /// <param name="redactedPayload">The payload that will be sent; returned unchanged when it is
    /// not a JSON object, because a session start must never be dropped.</param>
    /// <param name="projectKey">From <see cref="ResolveProjectKeyAsync"/>.</param>
    /// <param name="eventId">The queued start's id, which lets the server report a redelivery as a duplicate.</param>
    public static string Enrich(string redactedPayload, string? projectKey, string eventId)
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
        body["machineName"] = FieldLimits.Clamp(Environment.MachineName, FieldLimits.MachineName);
        body["projectKey"] = FieldLimits.Clamp(projectKey, FieldLimits.ProjectKey);
        body["developerEmail"] = FieldLimits.Email(AgentPayload.StringValue(body, "developerEmail"));
        body["eventId"] = eventId;
        return body.ToJsonString();
    }

    /// <summary>Whether this start may reopen a session kept on this machine. Only a new session or a
    /// resume is the developer coming back to it; a compact or clear start is the same sitting continuing.
    /// A start with no source is from a hook that predates the field. Any other source, including the hook's
    /// "unknown" for a value it could not forward, keeps the session here.</summary>
    public static bool MayReopen(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            if (!doc.RootElement.TryGetProperty("source", out var source) || source.ValueKind == JsonValueKind.Null)
                return true;
            return source.ValueKind == JsonValueKind.String
                && source.GetString() is { } value
                && (value.Equals("startup", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("resume", StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return false;
        }
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
}
