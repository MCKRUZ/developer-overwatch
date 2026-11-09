using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>Reads and adjusts queued hook payloads on their way to the server.</summary>
internal static class AgentPayload
{
    private static readonly string[] ItemLists = ["conversationTurns", "toolExecutions", "fileChanges"];

    /// <summary>The ids of every item a ToolExecution or FileChange payload carries. The server reports
    /// each item's outcome by its id. Empty when the payload is not a JSON object.</summary>
    public static IReadOnlyList<string> ItemIds(string payloadJson) =>
        ParseObject(payloadJson) is { } body
            ? ItemLists.SelectMany(list => Items(body, list)).Select(item => StringValue(item, "id")).OfType<string>().ToList()
            : [];

    /// <summary>Clamps each tool execution's name to the server limit. The payload is returned unchanged
    /// when nothing needed clamping.</summary>
    public static string NormalizeToolEvent(string payloadJson)
    {
        if (ParseObject(payloadJson) is not { } body)
            return payloadJson;

        var changed = false;
        foreach (var item in Items(body, "toolExecutions"))
        {
            if (StringValue(item, "toolName") is { Length: > FieldLimits.Name } name)
            {
                item["toolName"] = FieldLimits.Clamp(name, FieldLimits.Name);
                changed = true;
            }
        }

        return changed ? body.ToJsonString() : payloadJson;
    }

    /// <summary>Adds the queued event's id. The server records it, so a redelivered start or end comes
    /// back as a duplicate instead of being applied twice. Unchanged when not a JSON object.</summary>
    public static string WithEventId(string payloadJson, string eventId)
    {
        if (ParseObject(payloadJson) is not { } body)
            return payloadJson;

        body["eventId"] = eventId;
        return body.ToJsonString();
    }

    public static JsonObject? ParseObject(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? StringValue(JsonObject obj, string name) =>
        obj[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static IEnumerable<JsonObject> Items(JsonObject body, string list) =>
        body[list] is JsonArray array ? array.OfType<JsonObject>() : [];
}
