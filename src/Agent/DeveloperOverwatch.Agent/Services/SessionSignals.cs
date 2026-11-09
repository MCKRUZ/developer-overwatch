using System.Globalization;
using System.Text.Json;
using DeveloperOverwatch.Agent.Models;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>Builds the <c>/events</c> payload that carries a session's activity states and failed
/// tool calls. Activity comes from the hooks' own ActivityChanged events and is also derived: a
/// ToolExecution means tools are running (active), and the Stop marker (ConversationTurn) means the
/// turn ended and the user has the ball (idle). The server keeps the newest state, so order and
/// duplicates do not matter here. Tool failures have no ids, so the caller must send this payload
/// once per event, which is why it ships last and only when everything before it succeeded.</summary>
internal static class SessionSignals
{
    private const int MaxToolNameLength = 255;

    private static readonly HashSet<string> States = ["active", "waiting", "idle"];

    public static bool IsSignalEvent(EventType type) =>
        type is EventType.ActivityChanged or EventType.ToolFailed;

    /// <summary>Null when the events carry no activity and no tool failure.</summary>
    public static string? BuildPayload(string sessionId, IReadOnlyList<QueuedEvent> events)
    {
        var activityEvents = events.Select(ToActivity).OfType<ActivityEvent>().ToList();
        var toolFailures = events
            .Where(e => e.EventType == EventType.ToolFailed)
            .Select(ToToolFailure)
            .OfType<ToolFailure>()
            .ToList();

        if (activityEvents.Count == 0 && toolFailures.Count == 0)
            return null;

        return JsonSerializer.Serialize(new
        {
            sessionId,
            activityEvents = activityEvents.Select(a => new { state = a.State, at = Iso(a.At) }),
            toolFailures = toolFailures.Select(f => new { toolName = f.ToolName, at = Iso(f.At) })
        });
    }

    private sealed record ActivityEvent(string State, DateTime At);

    private sealed record ToolFailure(string ToolName, DateTime At);

    private static ActivityEvent? ToActivity(QueuedEvent evt) => evt.EventType switch
    {
        EventType.ToolExecution => new ActivityEvent("active", evt.CreatedAt),
        // The Stop hook's marker carries no "at": idle as of when it was queued. Backfill's marker is queued
        // long after the session, so it carries the transcript's last activity.
        EventType.ConversationTurn => new ActivityEvent("idle", ReadPayload(evt) is { } marker ? ReadAt(marker, evt) : evt.CreatedAt),
        EventType.ActivityChanged => ReadPayload(evt) is { } p
            && p.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.String
            && States.Contains(s.GetString()!)
                ? new ActivityEvent(s.GetString()!, ReadAt(p, evt))
                : null,
        _ => null
    };

    private static ToolFailure? ToToolFailure(QueuedEvent evt)
    {
        if (ReadPayload(evt) is not { } p
            || !p.TryGetProperty("toolName", out var n) || n.ValueKind != JsonValueKind.String
            || n.GetString() is not { Length: > 0 } name)
            return null;

        return new ToolFailure(name.Length > MaxToolNameLength ? name[..MaxToolNameLength] : name, ReadAt(p, evt));
    }

    private static JsonElement? ReadPayload(QueuedEvent evt)
    {
        try
        {
            using var doc = JsonDocument.Parse(evt.PayloadJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateTime ReadAt(JsonElement payload, QueuedEvent evt) =>
        payload.TryGetProperty("at", out var at) && at.ValueKind == JsonValueKind.String
        && DateTime.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime()
            : evt.CreatedAt;

    private static string Iso(DateTime value) =>
        DateTime.SpecifyKind(value, value.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : value.Kind)
            .ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
}
