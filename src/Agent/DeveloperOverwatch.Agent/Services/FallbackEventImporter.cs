using System.Text.Json;
using DeveloperOverwatch.Agent.Models;
using Microsoft.Extensions.Logging;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>Moves the events hooks wrote to <c>queue-fallback.jsonl</c> into the queue. Hooks
/// write there because they cannot load the SQLite library.</summary>
internal static class FallbackEventImporter
{
    public static async Task ImportAsync(string overwatchDir, IEventQueue queue, ILogger logger)
    {
        var fallbackPath = Path.Combine(overwatchDir, "queue-fallback.jsonl");
        if (!File.Exists(fallbackPath))
            return;

        try
        {
            var lines = await File.ReadAllLinesAsync(fallbackPath);
            var imported = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    await queue.EnqueueAsync(Parse(line));
                    imported++;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Skipping malformed fallback line");
                }
            }

            if (imported > 0)
            {
                logger.LogInformation("Imported {Count} events from fallback file", imported);
                File.Delete(fallbackPath);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to import fallback events");
        }
    }

    private static QueuedEvent Parse(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        return new QueuedEvent
        {
            Id = root.GetProperty("id").GetString()!,
            SessionId = root.GetProperty("sessionId").GetString()!,
            EventType = Enum.Parse<EventType>(root.GetProperty("eventType").GetString()!),
            PayloadJson = root.TryGetProperty("payload", out var p) ? p.GetString()! : "{}",
            CreatedAt = DateTime.Parse(root.GetProperty("createdAt").GetString()!,
                null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }
}
