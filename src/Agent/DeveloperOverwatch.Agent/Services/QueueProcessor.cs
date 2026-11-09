namespace DeveloperOverwatch.Agent.Services;

using System.Text.Json;
using DeveloperOverwatch.Agent.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class QueueProcessor : BackgroundService
{
    private static readonly TimeSpan ShippedRetention = TimeSpan.FromHours(24);

    // Local usage, and events a machine never managed to ship, are kept this long and no longer.
    private static readonly TimeSpan LocalRetention = TimeSpan.FromDays(30);

    private readonly IEventQueue _queue;
    private readonly IApiClient _api;
    private readonly IPrivacyRedactor _redactor;
    private readonly IGitFallbackWriter _gitFallback;
    private readonly IConversationCapture _conversationCapture;
    private readonly IConversationCursorStore _cursorStore;
    private readonly IGitRemoteReader _gitRemote;
    private readonly IUsageStore _usageStore;
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
        IGitRemoteReader gitRemote,
        IUsageStore usageStore,
        AgentConfig config,
        ILogger<QueueProcessor> logger)
    {
        _queue = queue;
        _api = api;
        _redactor = redactor;
        _gitFallback = gitFallback;
        _conversationCapture = conversationCapture;
        _cursorStore = cursorStore;
        _gitRemote = gitRemote;
        _usageStore = usageStore;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _queue.InitializeAsync();
        await _usageStore.InitializeAsync();
        _logger.LogInformation("Queue processor started in {Mode} mode, polling every {Interval}s",
            _config.Mode, _config.PollIntervalSeconds);

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

            if (_config.Mode == AgentMode.Local)
            {
                await RecordSessionEventsLocallyAsync(sessionId, events, ct);
                await _queue.MarkShippedAsync(events.Select(e => e.Id).ToList());
                continue;
            }

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

    /// <summary>Local mode sends nothing anywhere. The session's project and its usage are kept in
    /// queue.db; the caller then marks the events shipped, meaning handled, so the queue drains.</summary>
    private async Task RecordSessionEventsLocallyAsync(
        string sessionId, IReadOnlyList<QueuedEvent> events, CancellationToken ct)
    {
        foreach (var evt in events)
        {
            if (evt.EventType == EventType.SessionStart)
                await RecordSessionProjectAsync(sessionId, evt, ct);
            else if (evt.EventType == EventType.ConversationTurn
                     && await CaptureAndRecordUsageAsync(sessionId, evt, ct) is { } captured)
                await AdvanceCursorsAsync(captured.Captures, ct);
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
                EventType.SessionStart => await ShipSessionStartAsync(sessionId, evt, redacted, ct),
                EventType.SessionEnd => await _api.SendSessionEndAsync(sessionId, redacted, ct),
                EventType.ConversationTurn => await CaptureAndShipConversationAsync(sessionId, evt, ct),
                _ => await _api.SendSessionEventsAsync(sessionId, redacted, ct)
            };

            if (!success)
                return false;
        }

        return true;
    }

    private async Task<bool> ShipSessionStartAsync(
        string sessionId, QueuedEvent evt, string redacted, CancellationToken ct)
    {
        var projectKey = await RecordSessionProjectAsync(sessionId, evt, ct);
        return await _api.SendSessionStartAsync(SessionStartPayload.Enrich(redacted, projectKey), ct);
    }

    private async Task<string?> RecordSessionProjectAsync(string sessionId, QueuedEvent evt, CancellationToken ct)
    {
        var projectKey = await SessionStartPayload.ResolveProjectKeyAsync(evt.PayloadJson, _gitRemote, ct);
        await _usageStore.SetSessionProjectAsync(sessionId, projectKey, ct);
        return projectKey;
    }

    private const int MaxTurnContentLength = 4096;

    private async Task<bool> CaptureAndShipConversationAsync(
        string sessionId, QueuedEvent evt, CancellationToken ct)
    {
        if (await CaptureAndRecordUsageAsync(sessionId, evt, ct) is not { } captured)
            return true;

        if (captured.Turns.Count > 0)
        {
            var sessionTitle = await ResolveSessionTitleAsync(captured.TranscriptPath, ct);
            var payload = BuildConversationPayload(sessionId, sessionTitle, captured.Turns);
            if (!await _api.SendSessionEventsAsync(sessionId, _redactor.Redact(payload), ct))
                return false;
        }

        // Cursors move only after the server has the turns. If a later cursor write fails, the
        // resend carries the same stable turn ids and the server skips what it already stored.
        await AdvanceCursorsAsync(captured.Captures, ct);
        return true;
    }

    private sealed record TranscriptCapture(string CursorKey, ConversationCaptureResult Result);

    private sealed record CapturedConversation(
        string TranscriptPath, IReadOnlyList<TranscriptCapture> Captures, IReadOnlyList<CapturedTurn> Turns);

    /// <summary>Captures the new turns and records their usage on this machine before anything is
    /// sent, so a failed send never loses it; re-capture after a failure is deduplicated by the
    /// turns' stable ids. Null when the marker names no transcript.</summary>
    private async Task<CapturedConversation?> CaptureAndRecordUsageAsync(
        string sessionId, QueuedEvent evt, CancellationToken ct)
    {
        var transcriptPath = ReadTranscriptPath(evt.PayloadJson);
        if (string.IsNullOrEmpty(transcriptPath))
            return null;

        var captures = await CaptureTranscriptsAsync(sessionId, transcriptPath, ct);
        // The server numbers turns in arrival order, so interleave subagent work with the parent
        // conversation by time. OrderBy is stable: same-timestamp turns keep transcript order.
        var turns = captures
            .SelectMany(c => c.Result.Turns)
            .OrderBy(t => t.Timestamp ?? DateTime.MaxValue)
            .ToList();

        await _usageStore.RecordTurnsAsync(sessionId, turns, ct);
        return new CapturedConversation(transcriptPath, captures, turns);
    }

    private string? ReadTranscriptPath(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.TryGetProperty("transcriptPath", out var tp) ? tp.GetString() : null;
        }
        catch (JsonException)
        {
            _logger.LogDebug("Malformed ConversationTurn marker payload, skipping");
            return null;
        }
    }

    private async Task AdvanceCursorsAsync(IEnumerable<TranscriptCapture> captures, CancellationToken ct)
    {
        foreach (var capture in captures)
            await _cursorStore.SetAsync(capture.CursorKey, capture.Result.TotalLines, ct);
    }

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
                gitBranch = ClampLabel(t.GitBranch),
                subagentName = ClampLabel(t.SubagentName),
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

    // The server rejects longer values, and a rejected batch would be retried forever.
    private const int MaxLabelLength = 255;

    private static string? ClampLabel(string? value) =>
        value is { Length: > MaxLabelLength } ? value[..MaxLabelLength] : value;

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
        // The fallback commits events into the repository, and a push would carry them off the
        // machine — so it never runs in local mode, whoever calls it.
        if (_config.Mode == AgentMode.Local)
            return;

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
        await _queue.PurgeShippedAsync(ShippedRetention);
        // A pilot machine that stays offline must not grow its queue forever.
        await _queue.PurgeUnshippedAsync(LocalRetention);
        await _usageStore.PurgeAsync(LocalRetention);
    }

    private Task ImportFallbackEventsAsync() =>
        FallbackEventImporter.ImportAsync(_config.OverwatchDir, _queue, _logger);

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
