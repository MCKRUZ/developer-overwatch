using System.Globalization;
using System.Text.Json;
using DeveloperOverwatch.Agent.Models;
using Microsoft.Extensions.Logging;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>Moves the events hooks wrote to <c>queue-fallback.jsonl</c> into the queue. Hooks
/// write there because they cannot load the SQLite library.</summary>
/// <remarks>
/// <para>The file is first claimed by an atomic rename, so a hook appending while the import runs
/// writes to a fresh file that a later poll picks up, instead of into a file about to be deleted.</para>
/// <para>A claimed file is read only once it has been claimed for <see cref="ClaimSettleTime"/>. On
/// Linux and macOS the rename succeeds while a hook still has the file open, and that hook's line
/// lands in the claimed file; reading the file at once could delete it before the line arrives. The
/// claim time is in the file name because a rename does not change a file's last write time.</para>
/// <para>Claimed files left by a crash, or by a queue write that threw, are imported on a later
/// poll; enqueue ignores ids it already has, so importing a file twice is harmless. A claimed file
/// whose name carries no claim time, as an older agent named them, counts as settled.</para>
/// </remarks>
public static class FallbackEventImporter
{
    public static readonly TimeSpan ClaimSettleTime = TimeSpan.FromSeconds(2);

    private const string FallbackFileName = "queue-fallback.jsonl";
    private const string ClaimedPrefix = "queue-fallback.";
    private const string ClaimedSuffix = ".importing";
    private const string ClaimedPattern = ClaimedPrefix + "*" + ClaimedSuffix;

    private static readonly long MaxUnixMilliseconds = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

    public static async Task ImportAsync(string overwatchDir, IEventQueue queue, ILogger logger, TimeProvider time)
    {
        if (!Directory.Exists(overwatchDir))
            return;

        var now = time.GetUtcNow();
        TryClaim(overwatchDir, now, logger);

        var settled = Directory.EnumerateFiles(overwatchDir, ClaimedPattern)
            .Select(path => (Path: path, ClaimedAt: ReadClaimTime(path)))
            .Where(claim => claim.ClaimedAt <= now - ClaimSettleTime)
            .OrderBy(claim => claim.ClaimedAt)
            .ToList();

        foreach (var claim in settled)
            await ImportClaimedAsync(claim.Path, queue, logger);
    }

    private static void TryClaim(string overwatchDir, DateTimeOffset now, ILogger logger)
    {
        var fallbackPath = Path.Combine(overwatchDir, FallbackFileName);
        var claimedPath = Path.Combine(
            overwatchDir, $"{ClaimedPrefix}{now.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}{ClaimedSuffix}");

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

    /// <summary>The claim time from a name like <c>queue-fallback.{unixMs}-{guid}.importing</c>; the
    /// earliest possible time for a name without one, so it is imported at once.</summary>
    private static DateTimeOffset ReadClaimTime(string claimedPath)
    {
        var name = Path.GetFileName(claimedPath);
        if (name.Length < ClaimedPrefix.Length + ClaimedSuffix.Length)
            return DateTimeOffset.MinValue;

        var stamp = name[ClaimedPrefix.Length..^ClaimedSuffix.Length];
        var dash = stamp.IndexOf('-');
        return dash > 0
            && long.TryParse(stamp[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out var ms)
            && ms <= MaxUnixMilliseconds
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                : DateTimeOffset.MinValue;
    }

    private const int MaxLineLength = 1024 * 1024;

    private static async Task ImportClaimedAsync(string claimedPath, IEventQueue queue, ILogger logger)
    {
        try
        {
            var imported = 0;

            // Streamed, so a huge file never sits in memory at once. No hook writes a line this long.
            foreach (var line in File.ReadLines(claimedPath))
            {
                if (line.Length > MaxLineLength)
                {
                    logger.LogWarning("Skipped a {Length}-character line in fallback file {Path}", line.Length, claimedPath);
                    continue;
                }

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
                    null, DateTimeStyles.RoundtripKind)
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Skipping malformed fallback line");
            return null;
        }
    }
}
