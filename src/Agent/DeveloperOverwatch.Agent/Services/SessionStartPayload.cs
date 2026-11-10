using System.Globalization;
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
    /// not a JSON object, because a session start must never be dropped on an open session.</param>
    /// <param name="projectKey">From <see cref="ResolveProjectKeyAsync"/>.</param>
    /// <param name="eventId">The queued start's id, which lets the server report a redelivery as a duplicate.</param>
    /// <param name="session">The title goes only when the hook shows it was written at or above the session's
    /// floor; a title from below names the stretch the session was held. With a floor, an undated title is dropped.</param>
    public static string Enrich(string redactedPayload, string? projectKey, string eventId, OpenSession session)
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
        if (!session.Admits(WrittenAt(AgentPayload.StringValue(body, "titleWrittenAt"))))
            body.Remove("title");
        body.Remove("titleWrittenAt");
        return body.ToJsonString();
    }

    private static DateTime? WrittenAt(string? writtenAt) =>
        DateTime.TryParse(writtenAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : null;

    /// <summary>Whether this start may open a session in <paramref name="state"/>. A new session or a resume is the
    /// developer coming to it. A fork (<c>--fork-session</c>, <c>/fork</c>, <c>/branch</c>, a session moved to the
    /// background) writes a new session id, so it opens only a session this machine has never decided on: a fork
    /// that reused a known id would otherwise reopen a closed session without a resume. A clear opens only a
    /// session this machine has never decided on; in a known
    /// session it is the same sitting continuing. So does a start with no source, from a hook or Claude Code
    /// version that predates the field: it cannot show the developer came back. Any other source, including the
    /// hook's "unknown", never opens.</summary>
    public static bool MayOpen(string payloadJson, AccessState state)
    {
        if (!TryReadSource(payloadJson, out var value))
            return false;
        if (value is null)
            return state == AccessState.New;
        if (value.Equals("startup", StringComparison.OrdinalIgnoreCase)
            || value.Equals("resume", StringComparison.OrdinalIgnoreCase))
            return true;
        return state == AccessState.New
            && (value.Equals("clear", StringComparison.OrdinalIgnoreCase)
                || value.Equals("fork", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The instant a start opens a session in <paramref name="state"/> from. A session first seen here
    /// through a new-session or clear start began here, so it opens from enrolment and keeps a first prompt written
    /// just before the hook stamped the start. Any other start shows the session existed before it, so it opens at
    /// the start.</summary>
    public static DateTime OpenFrom(string payloadJson, DateTime createdAt, AccessState state, DateTime? pilotSince) =>
        state == AccessState.New
        && TryReadSource(payloadJson, out var source)
        && source?.ToLowerInvariant() is "startup" or "clear"
            ? pilotSince ?? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)
            : UtcInstant.Utc(createdAt);

    /// <returns>False when the payload is not JSON or not an object, or its source is not a string. Otherwise
    /// true, with <paramref name="source"/> null when the source is missing or JSON null.</returns>
    private static bool TryReadSource(string payloadJson, out string? source)
    {
        source = null;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            if (!doc.RootElement.TryGetProperty("source", out var element) || element.ValueKind == JsonValueKind.Null)
                return true;
            if (element.ValueKind != JsonValueKind.String)
                return false;
            source = element.GetString();
            return true;
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
