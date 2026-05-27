namespace DeveloperOverwatch.Agent.Services;

using System.Text.Json;
using DeveloperOverwatch.Agent.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class QueueProcessor : BackgroundService
{
    private readonly IEventQueue _queue;
    private readonly IApiClient _api;
    private readonly IPrivacyRedactor _redactor;
    private readonly IGitFallbackWriter _gitFallback;
    private readonly AgentConfig _config;
    private readonly ILogger<QueueProcessor> _logger;

    private int _consecutiveFailures;

    public QueueProcessor(
        IEventQueue queue,
        IApiClient api,
        IPrivacyRedactor redactor,
        IGitFallbackWriter gitFallback,
        AgentConfig config,
        ILogger<QueueProcessor> logger)
    {
        _queue = queue;
        _api = api;
        _redactor = redactor;
        _gitFallback = gitFallback;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _queue.InitializeAsync();
        _logger.LogInformation("Queue processor started, polling every {Interval}s", _config.PollIntervalSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_config.PollIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ImportFallbackEventsAsync();
                await ProcessBatchAsync(stoppingToken);
                await PurgeOldEventsAsync();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing queue batch");
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        var batch = await _queue.DequeueBatchAsync(_config.BatchSize);
        if (batch.Count == 0)
            return;

        _logger.LogDebug("Processing batch of {Count} events", batch.Count);

        var grouped = batch.GroupBy(e => e.SessionId);

        foreach (var sessionGroup in grouped)
        {
            var sessionId = sessionGroup.Key;
            var events = sessionGroup.ToList();
            var shipped = await ShipSessionEventsAsync(sessionId, events, ct);

            if (shipped)
            {
                await _queue.MarkShippedAsync(events.Select(e => e.Id).ToList());
                _consecutiveFailures = 0;
            }
            else
            {
                _consecutiveFailures++;
                foreach (var evt in events)
                    await _queue.IncrementRetryAsync(evt.Id);

                await CheckGitFallbackAsync(sessionId, ct);
                await ApplyBackoffAsync(ct);
            }
        }
    }

    private async Task<bool> ShipSessionEventsAsync(
        string sessionId, IReadOnlyList<QueuedEvent> events, CancellationToken ct)
    {
        foreach (var evt in events)
        {
            var redacted = _redactor.Redact(evt.PayloadJson);

            var success = evt.EventType switch
            {
                EventType.SessionStart => await _api.SendSessionStartAsync(redacted, ct),
                EventType.SessionEnd => await _api.SendSessionEndAsync(sessionId, redacted, ct),
                _ => await _api.SendSessionEventsAsync(sessionId, redacted, ct)
            };

            if (!success)
                return false;
        }

        return true;
    }

    private async Task CheckGitFallbackAsync(string sessionId, CancellationToken ct)
    {
        var unshipped = await _queue.GetUnshippedCountAsync();
        if (unshipped < _config.GitFallbackThreshold)
            return;

        _logger.LogWarning(
            "Queue has {Count} unshipped events (threshold: {Threshold}), writing git fallback",
            unshipped, _config.GitFallbackThreshold);

        var allUnshipped = await _queue.DequeueBatchAsync(unshipped);
        var repoPath = await GetActiveRepoPathAsync();

        if (repoPath is not null)
        {
            var written = await _gitFallback.WriteFallbackAsync(sessionId, allUnshipped, repoPath, ct);
            if (written)
            {
                await _gitFallback.AddGitTrailerAsync(sessionId, repoPath, ct);
                await _queue.MarkShippedAsync(allUnshipped.Select(e => e.Id).ToList());
            }
        }
    }

    private async Task ApplyBackoffAsync(CancellationToken ct)
    {
        var delaySeconds = Math.Min(Math.Pow(2, _consecutiveFailures), 60);
        _logger.LogDebug("Backing off for {Delay}s (failure #{Count})", delaySeconds, _consecutiveFailures);
        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
    }

    private async Task PurgeOldEventsAsync()
    {
        await _queue.PurgeShippedAsync(TimeSpan.FromHours(24));
    }

    private async Task ImportFallbackEventsAsync()
    {
        var fallbackPath = Path.Combine(_config.OverwatchDir, "queue-fallback.jsonl");
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
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    var evt = new QueuedEvent
                    {
                        Id = root.GetProperty("id").GetString()!,
                        SessionId = root.GetProperty("sessionId").GetString()!,
                        EventType = Enum.Parse<EventType>(root.GetProperty("eventType").GetString()!),
                        PayloadJson = root.TryGetProperty("payload", out var p) ? p.GetString()! : "{}",
                        CreatedAt = DateTime.Parse(root.GetProperty("createdAt").GetString()!,
                            null, System.Globalization.DateTimeStyles.RoundtripKind)
                    };

                    await _queue.EnqueueAsync(evt);
                    imported++;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Skipping malformed fallback line");
                }
            }

            if (imported > 0)
            {
                _logger.LogInformation("Imported {Count} events from fallback file", imported);
                File.Delete(fallbackPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to import fallback events");
        }
    }

    private Task<string?> GetActiveRepoPathAsync()
    {
        try
        {
            var path = _config.ActiveSessionPath.Replace("active-session", "active-repo");
            if (File.Exists(path))
                return Task.FromResult<string?>(File.ReadAllText(path).Trim());
        }
        catch
        {
            // best effort
        }
        return Task.FromResult<string?>(null);
    }
}
