using System.Text.Json;
using DeveloperOverwatch.Agent.Models;
using Microsoft.Extensions.Logging;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>Moves the events hooks wrote to <c>queue-fallback.jsonl</c> into the queue. Hooks
/// write there because they cannot load the SQLite library.</summary>
/// <remarks>The file is first claimed by an atomic rename, so a hook appending while the import
/// runs writes to a fresh file that the next poll picks up, instead of into a file about to be
/// deleted. Claimed files left by a crash, or by a queue write that threw, are imported on the
/// next poll; enqueue ignores ids it already has, so importing a file twice is harmless.</remarks>
public static class FallbackEventImporter
{
    private const string FallbackFileName = "queue-fallback.jsonl";
    private const string ClaimedPattern = "queue-fallback.*.importing";

    public static async Task ImportAsync(string overwatchDir, IEventQueue queue, ILogger logger)
    {
        if (!Directory.Exists(overwatchDir))
            return;

        TryClaim(overwatchDir, logger);

        foreach (var claimed in Directory.EnumerateFiles(overwatchDir, ClaimedPattern).ToList())
            await ImportClaimedAsync(claimed, queue, logger);
    }

    private static void TryClaim(string overwatchDir, ILogger logger)
    {
        var fallbackPath = Path.Combine(overwatchDir, FallbackFileName);
        var claimedPath = Path.Combine(overwatchDir, $"queue-fallback.{Guid.NewGuid():N}.importing");

        try
        {
            File.Move(fallbackPath, claimedPath);
        }
        catch (FileNotFoundException)
        {
            // Nothing queued since the last poll.
        }
        catch (IOException ex)
        {
            // A hook has the file open; it will be claimed on a later poll.
            logger.LogDebug(ex, "Fallback file busy, skipping this poll");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Fallback file busy, skipping this poll");
        }
    }

    private static async Task ImportClaimedAsync(string claimedPath, IEventQueue queue, ILogger logger)
    {
        try
        {
            var lines = await File.ReadAllLinesAsync(claimedPath);
            var imported = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || TryParse(line, logger) is not { } evt)
                    continue;

                // A throw here keeps the claimed file for the next poll.
                await queue.EnqueueAsync(evt);
                imported++;
            }

            // Deleted even when some lines were malformed: they would never parse on a retry.
            File.Delete(claimedPath);
            if (imported > 0)
                logger.LogInformation("Imported {Count} events from fallback file", imported);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to import fallback events from {Path}", claimedPath);
        }
    }

    private static QueuedEvent? TryParse(string line, ILogger logger)
    {
        try
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
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Skipping malformed fallback line");
            return null;
        }
    }
}
