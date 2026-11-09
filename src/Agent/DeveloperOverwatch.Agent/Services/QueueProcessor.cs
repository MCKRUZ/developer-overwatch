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
    private readonly IConversationCapture _conversationCapture;
    private readonly IConversationCursorStore _cursorStore;
    private readonly AgentConfig _config;
    private readonly ILogger<QueueProcessor> _logger;

    private int _consecutiveFailures;

    public QueueProcessor(
        IEventQueue queue,
        IApiClient api,
        IPrivacyRedactor redactor,
        IGitFallbackWriter gitFallback,
        IConversationCapture conversationCapture,
        IConversationCursorStore cursorStore,
        AgentConfig config,
        ILogger<QueueProcessor> logger)
    {
        _queue = queue;
        _api = api;
        _redactor = redactor;
        _gitFallback = gitFallback;
        _conversationCapture = conversationCapture;
        _cursorStore = cursorStore;
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
                EventType.ConversationTurn => await CaptureAndShipConversationAsync(sessionId, evt, ct),
                _ => await _api.SendSessionEventsAsync(sessionId, redacted, ct)
            };

            if (!success)
                return false;
        }

        return true;
    }

    private const int MaxTurnContentLength = 4096;

    private async Task<bool> CaptureAndShipConversationAsync(
        string sessionId, QueuedEvent evt, CancellationToken ct)
    {
        string? transcriptPath = null;
        try
        {
            using var doc = JsonDocument.Parse(evt.PayloadJson);
            if (doc.RootElement.TryGetProperty("transcriptPath", out var tp))
                transcriptPath = tp.GetString();
        }
        catch (JsonException)
        {
            _logger.LogDebug("Malformed ConversationTurn marker payload, skipping");
        }

        if (string.IsNullOrEmpty(transcriptPath))
            return true;

        var captures = await CaptureTranscriptsAsync(sessionId, transcriptPath, ct);
        // The server numbers turns in arrival order, so interleave subagent work with the parent
        // conversation by time. OrderBy is stable: same-timestamp turns keep transcript order.
        var turns = captures
            .SelectMany(c => c.Result.Turns)
            .OrderBy(t => t.Timestamp ?? DateTime.MaxValue)
            .ToList();

        if (turns.Count > 0)
        {
            var sessionTitle = await ResolveSessionTitleAsync(transcriptPath, ct);
            var payload = BuildConversationPayload(sessionId, sessionTitle, turns);
            if (!await _api.SendSessionEventsAsync(sessionId, _redactor.Redact(payload), ct))
                return false;
        }

        // Cursors move only after the server has the turns. If a later cursor write fails, the
        // resend carries the same stable turn ids and the server skips what it already stored.
        foreach (var capture in captures)
            await _cursorStore.SetAsync(capture.CursorKey, capture.Result.TotalLines, ct);

        return true;
    }

    private sealed record TranscriptCapture(string CursorKey, ConversationCaptureResult Result);

    /// <summary>Reads the session transcript plus every subagent transcript beside it. A subagent's
    /// real usage — with its input/output/cache split and its own model — is only in its own file;
    /// the parent's completion notice reports just the subagent's final context size.</summary>
    private async Task<List<TranscriptCapture>> CaptureTranscriptsAsync(
        string sessionId, string transcriptPath, CancellationToken ct)
    {
        var mainCursor = await _cursorStore.GetAsync(sessionId, ct);
        var captures = new List<TranscriptCapture>
        {
            new(sessionId, await _conversationCapture.CaptureFromFileAsync(transcriptPath, mainCursor, false, ct))
        };

        foreach (var subagentPath in _conversationCapture.FindSubagentTranscripts(transcriptPath))
        {
            var cursorKey = $"{sessionId}/{Path.GetFileNameWithoutExtension(subagentPath)}";
            var cursor = await _cursorStore.GetAsync(cursorKey, ct);
            captures.Add(new(cursorKey, await _conversationCapture.CaptureFromFileAsync(subagentPath, cursor, true, ct)));
        }

        return captures;
    }

    private string BuildConversationPayload(string sessionId, string? sessionTitle, IReadOnlyList<CapturedTurn> turns) =>
        JsonSerializer.Serialize(new
        {
            sessionId,
            sessionTitle,
            conversationTurns = turns.Select((t, i) => new
            {
                id = StableTurnId.From(t.SourceId),
                turnIndex = i,
                role = t.Role,
                content = t.Content is { Length: > MaxTurnContentLength } longText ? longText[..MaxTurnContentLength] : t.Content,
                tokenCount = TotalTokens(t) is var total && total > 0 ? total : (int?)null,
                inputTokens = t.InputTokens,
                outputTokens = t.OutputTokens,
                cacheCreationInputTokens = t.CacheCreationInputTokens,
                cacheReadInputTokens = t.CacheReadInputTokens,
                model = t.Model,
                // Computed from the RAW (pre-redaction) content — by the time this payload is
                // redacted, the secret itself is gone, so this flag is the only signal the server
                // ever gets that one was present.
                containsRedactedSecret = t.Content is not null && _redactor.ContainsSensitive(t.Content),
                timestamp = (t.Timestamp ?? DateTime.UtcNow).ToString("o"),
                isDecision = false
            }),
            toolExecutions = Array.Empty<object>(),
            fileChanges = Array.Empty<object>()
        });

    // Cache reads and writes are tokens the model processed too — usually most of them.
    private static int TotalTokens(CapturedTurn t) =>
        (t.InputTokens ?? 0) + (t.OutputTokens ?? 0) + (t.CacheCreationInputTokens ?? 0) + (t.CacheReadInputTokens ?? 0);

    // Claude Code stores a `/rename`d session title in a sidecar file next to the transcript
    // (<projects-dir>/<encoded-repo>/<session-id>/custom-title.json), not in the transcript
    // itself — read it fresh on every capture so a mid-session rename shows up on the dashboard
    // instead of the raw session GUID.
    private static async Task<string?> ResolveSessionTitleAsync(string transcriptPath, CancellationToken ct)
    {
        try
        {
            var dir = Path.GetDirectoryName(transcriptPath);
            if (dir is null)
                return null;

            var sessionFolder = Path.GetFileNameWithoutExtension(transcriptPath);
            var titlePath = Path.Combine(dir, sessionFolder, "custom-title.json");
            if (!File.Exists(titlePath))
                return null;

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(titlePath, ct));
            return doc.RootElement.TryGetProperty("customTitle", out var titleProp)
                ? titleProp.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
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
        var repoPath = await GetRepoPathForSessionAsync(sessionId);

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

    /// <summary>
    /// Looks up the repo path for a given (our) session ID by scanning the per-Claude-session
    /// mapping files under ~/.overwatch/sessions/ (written by overwatch-session-start.ps1) —
    /// there's no direct sessionId -> file lookup since those files are keyed by Claude Code's
    /// own session_id, not ours.
    /// </summary>
    private async Task<string?> GetRepoPathForSessionAsync(string sessionId)
    {
        try
        {
            var sessionsDir = Path.Combine(_config.OverwatchDir, "sessions");
            if (!Directory.Exists(sessionsDir))
                return null;

            foreach (var file in Directory.EnumerateFiles(sessionsDir, "*.json"))
            {
                var json = await File.ReadAllTextAsync(file);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("ourSessionId", out var idProp) &&
                    idProp.GetString() == sessionId &&
                    doc.RootElement.TryGetProperty("repoPath", out var repoProp))
                {
                    return repoProp.GetString();
                }
            }
        }
        catch
        {
            // best effort
        }
        return null;
    }
}
