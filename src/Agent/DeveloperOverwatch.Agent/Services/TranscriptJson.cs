using System.Globalization;
using System.Text.Json;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>Tolerant reads of transcript JSON: a property that is missing or of another kind reads as absent.</summary>
internal static class TranscriptJson
{
    public static string? String(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;

    public static long? Long(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var value)
            ? value
            : null;

    public static bool IsTrue(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.True;

    public static bool TryObject(JsonElement obj, string name, out JsonElement value) =>
        obj.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object;

    public static bool HasArray(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Array;

    public static IEnumerable<JsonElement> Objects(JsonElement obj, string name) =>
        HasArray(obj, name)
            ? obj.GetProperty(name).EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
            : [];

    /// <summary>Every item of the array in place, null where it is not a string, so pairs by index stay paired.</summary>
    public static IReadOnlyList<string?> Strings(JsonElement obj, string name) =>
        HasArray(obj, name)
            ? obj.GetProperty(name).EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
                .ToList()
            : [];

    /// <summary>The line's <c>timestamp</c> as UTC.</summary>
    public static DateTime? Timestamp(JsonElement obj) =>
        String(obj, "timestamp") is { } text
        && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
            ? at
            : null;
}
