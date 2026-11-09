using System.Globalization;
using System.Text.Json;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>The working folder and start time of a Claude Code session: the first <c>cwd</c> and the first
/// <c>timestamp</c> in its transcript. Reading stops as soon as both are found.</summary>
internal sealed record TranscriptHeader(string? Cwd, DateTime? StartedAt)
{
    public static async Task<TranscriptHeader> ReadAsync(string transcriptPath, CancellationToken ct)
    {
        string? cwd = null;
        DateTime? startedAt = null;
        try
        {
            using var stream = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            while ((cwd is null || startedAt is null) && await reader.ReadLineAsync(ct) is { } line)
            {
                if (TryParseObject(line) is not { } root)
                    continue;

                using (root)
                {
                    cwd ??= ReadString(root.RootElement, "cwd");
                    startedAt ??= ParseUtc(ReadString(root.RootElement, "timestamp"));
                }
            }
        }
        catch (IOException)
        {
            // An unreadable transcript yields no header; capture reports its own failure.
        }

        return new TranscriptHeader(cwd, startedAt);
    }

    /// <summary>The <c>/rename</c>d title Claude Code keeps in <c>&lt;session-id&gt;/custom-title.json</c>
    /// beside the transcript, as overwatch-session-start.ps1 reads it; null when there is none.</summary>
    public static string? ReadCustomTitle(string transcriptPath)
    {
        var path = Path.Combine(
            Path.GetDirectoryName(transcriptPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(transcriptPath),
            "custom-title.json");
        try
        {
            if (!File.Exists(path) || TryParseObject(File.ReadAllText(path)) is not { } doc)
                return null;

            using (doc)
                return ReadString(doc.RootElement, "customTitle");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static JsonDocument? TryParseObject(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                return doc;

            doc.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            && prop.GetString() is { Length: > 0 } value
                ? value
                : null;

    private static DateTime? ParseUtc(string? value) =>
        value is not null && DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}
