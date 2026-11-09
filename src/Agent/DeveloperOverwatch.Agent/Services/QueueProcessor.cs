namespace DeveloperOverwatch.Agent.Services;

using DeveloperOverwatch.Agent.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class QueueProcessor : BackgroundService
{
    private static readonly TimeSpan ShippedRetention = TimeSpan.FromHours(24);

    // Events a machine never managed to ship are kept this long and no longer. Local usage is kept at
    // least this long, and always for the whole current month (see UsageStore.PurgeAsync).
    private static readonly TimeSpan LocalRetention = TimeSpan.FromDays(30);

    private readonly IEventQueue _queue;
    private readonly IApiClient _api;
    private readonly IPrivacyRedactor _redactor;
    private readonly IGitFallbackWriter _gitFallback;
    private readonly IConversationCapture _conversationCapture;
    private readonly IConversationCursorStore _cursorStore;
    private readonly IGitReader _gitRemote;
    private readonly IUsageStore _usageStore;
    private readonly AgentConfig _config;
    private readonly IAgentModeSource _modeSource;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<QueueProcessor> _logger;

    private int _consecutiveFailures;

    public QueueProcessor(
        IEventQueue queue,
        IApiClient api,
        IPrivacyRedactor redactor,
        IGitFallbackWriter gitFallback,
        IConversationCapture conversationCapture,
        IConversationCursorStore cursorStore,
        IGitReader gitRemote,
        IUsageStore usageStore,
        AgentConfig config,
        IAgentModeSource modeSource,
        IHostApplicationLifetime lifetime,
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
        _modeSource = modeSource;
        _lifetime = lifetime;
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
            var mode = ReadMode();
            if (mode is null)
                continue;
            if (mode != _config.Mode)
            {
                _logger.LogWarning("Agent mode changed from {Old} to {New}; stopping so a restart picks up the new config",
                    _config.Mode, mode);
                _lifetime.StopApplication();
                break;
            }

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

    /// <summary>The mode is read fresh before every poll: a machine switched to Local must stop shipping
    /// now, not at the next logon. A change stops the agent; the session-start hook starts it again with
    /// the new config. Null when the config cannot be read right now (locked, half-saved): that poll ships
    /// nothing, but the agent keeps running, since the file does not say the mode changed.</summary>
    private AgentMode? ReadMode()
    {
        try
        {
            if (_modeSource.CurrentMode() is { } mode)
                return mode;
            _logger.LogDebug("Could not parse the agent config; skipping this poll");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the agent mode; skipping this poll");
        }

        return null;
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        var batch = await _queue.DequeueBatchAsync(_config.BatchSize);
        if (batch.Count == 0)
            return;

        _logger.LogDebug("Processing batch of {Count} events", batch.Count);

        var anyFailed = false;
        foreach (var sessionGroup in batch.GroupBy(e => e.SessionId))
        {
            var sessionId = sessionGroup.Key;
            var events = sessionGroup.ToList();

            if (_config.Mode == AgentMode.Local)
            {
                await HandleLocallyAsync(sessionId, events, ct);
                continue;
            }

            switch (await ShipSessionEventsAsync(sessionId, events, ct))
            {
                case ApiResult.Success:
                    await _queue.MarkShippedAsync(Ids(events));
                    _consecutiveFailures = 0;
                    break;
                // A retry can never succeed for either, and the oldest group would block the queue, so
                // the group is never dequeued again. A session that was open when the machine enrolled
                // ends up here: the server learns of it at its next start or /resume.
                case ApiResult.NotFound:
                    _logger.LogWarning("Server does not know session {SessionId}; dropping its {Count} queued events",
                        sessionId, events.Count);
                    await _queue.MarkDeadAsync(Ids(events));
                    break;
                case ApiResult.Rejected:
                    _logger.LogWarning("Server rejected the events of session {SessionId}; dropping its {Count} queued events",
                        sessionId, events.Count);
                    await _queue.MarkDeadAsync(Ids(events));
                    break;
                default:
                    anyFailed = true;
                    foreach (var evt in events)
                        await _queue.IncrementRetryAsync(evt.Id);
                    await CheckGitFallbackAsync(sessionId, ct);
                    break;
            }
        }

        // Once per poll, so one failing session never delays the others in the batch.
        if (anyFailed)
        {
            _consecutiveFailures++;
            await ApplyBackoffAsync(ct);
        }
    }

    private static List<string> Ids(IEnumerable<QueuedEvent> events) => events.Select(e => e.Id).ToList();

    /// <summary>Local mode sends nothing anywhere. The session's project and its usage are kept in
    /// queue.db, and every event is marked shipped, meaning handled, so the queue drains. Pilot capture
    /// starts at enrolment: Local-era data leaves the machine only through an explicit usage-only
    /// backfill, never by shipping these events later.</summary>
    private async Task HandleLocallyAsync(string sessionId, IReadOnlyList<QueuedEvent> events, CancellationToken ct)
    {
        foreach (var evt in events)
        {
            if (evt.EventType == EventType.SessionStart)
                await RecordSessionProjectAsync(sessionId, evt, ct);
            else if (evt.EventType == EventType.ConversationTurn
                     && await CaptureAndRecordUsageAsync(sessionId, evt, ct) is { } captured)
                await AdvanceCursorsAsync(captured.Captures, ct);
        }

        await _queue.MarkShippedAsync(Ids(events));
    }

    private async Task<ApiResult> ShipSessionEventsAsync(
        string sessionId, IReadOnlyList<QueuedEvent> events, CancellationToken ct)
    {
        foreach (var evt in events)
        {
            var redacted = _redactor.Redact(evt.PayloadJson);

            var result = evt.EventType switch
            {
                EventType.SessionStart => await ShipSessionStartAsync(sessionId, evt, redacted, ct),
                EventType.SessionEnd => await _api.SendSessionEndAsync(sessionId, redacted, ct),
                EventType.ConversationTurn => await CaptureAndShipConversationAsync(sessionId, evt, ct),
                // Shipped together below, after everything else in the group.
                EventType.ActivityChanged or EventType.ToolFailed => ApiResult.Success,
                _ => await _api.SendSessionEventsAsync(sessionId, redacted, ct)
            };

            if (result != ApiResult.Success)
                return result;
        }

        // Last, so a failure earlier in the group never resends tool failures. The payload holds only
        // states, tool names, ids and times: nothing to redact.
        return SessionSignals.BuildPayload(sessionId, events) is { } signals
            ? await _api.SendSessionEventsAsync(sessionId, signals, ct)
            : ApiResult.Success;
    }

    private async Task<ApiResult> ShipSessionStartAsync(
        string sessionId, QueuedEvent evt, string redacted, CancellationToken ct)
    {
        var projectKey = await RecordSessionProjectAsync(sessionId, evt, ct);
        var result = await _api.SendSessionStartAsync(SessionStartPayload.Enrich(redacted, projectKey), ct);
        // A start names no existing session, so a 404 there is a server fault worth retrying.
        return result == ApiResult.NotFound ? ApiResult.Failed : result;
    }

    private async Task<string?> RecordSessionProjectAsync(string sessionId, QueuedEvent evt, CancellationToken ct)
    {
        var projectKey = await SessionStartPayload.ResolveProjectKeyAsync(evt.PayloadJson, _gitRemote, ct);
        await _usageStore.SetSessionProjectAsync(sessionId, projectKey, ct);
        return projectKey;
    }

    private async Task<ApiResult> CaptureAndShipConversationAsync(
        string sessionId, QueuedEvent evt, CancellationToken ct)
    {
        if (await CaptureAndRecordUsageAsync(sessionId, evt, ct) is not { } captured)
            return ApiResult.Success;

        if (captured.Turns.Count > 0)
        {
            var sessionTitle = await ConversationPayload.ResolveSessionTitleAsync(captured.Marker.TranscriptPath, ct);
            var payload = ConversationPayload.Build(
                sessionId, sessionTitle, captured.Turns, _redactor, captured.Marker.UsageOnly);
            var result = await _api.SendSessionEventsAsync(sessionId, _redactor.Redact(payload), ct);
            if (result != ApiResult.Success)
                return result;
        }

        // Cursors move only after the server has the turns. If a later cursor write fails, the
        // resend carries the same stable turn ids and the server skips what it already stored.
        await AdvanceCursorsAsync(captured.Captures, ct);
        return ApiResult.Success;
    }

    private sealed record TranscriptCapture(string CursorKey, ConversationCaptureResult Result);

    /// <param name="Captures">The live cursors to advance once the turns are stored; none for a
    /// usage-only marker.</param>
    private sealed record CapturedConversation(
        ConversationMarker Marker, IReadOnlyList<TranscriptCapture> Captures, IReadOnlyList<CapturedTurn> Turns);

    /// <summary>Captures the new turns and records their usage on this machine before anything is
    /// sent, so a failed send never loses it; re-capture after a failure is deduplicated by the
    /// turns' stable ids. Null when the marker names no transcript.</summary>
    private async Task<CapturedConversation?> CaptureAndRecordUsageAsync(
        string sessionId, QueuedEvent evt, CancellationToken ct)
    {
        if (ConversationMarker.Parse(evt.PayloadJson) is not { } marker)
        {
            _logger.LogDebug("ConversationTurn marker names no transcript, skipping");
            return null;
        }

        var captures = await CaptureTranscriptsAsync(sessionId, marker, ct);
        // The server numbers turns in arrival order, so interleave subagent work with the parent
        // conversation by time. OrderBy is stable: same-timestamp turns keep transcript order.
        var turns = captures
            .SelectMany(c => c.Result.Turns)
            .Where(t => marker.From is not { } from || (t.Timestamp is { } ts && ToUtc(ts) >= from))
            .OrderBy(t => t.Timestamp ?? DateTime.MaxValue)
            .ToList();

        await _usageStore.RecordTurnsAsync(sessionId, turns, ct);
        return new CapturedConversation(marker, marker.UsageOnly ? [] : captures, turns);
    }

    private async Task AdvanceCursorsAsync(IEnumerable<TranscriptCapture> captures, CancellationToken ct)
    {
        foreach (var capture in captures)
            await _cursorStore.SetAsync(capture.CursorKey, capture.Result.TotalLines, ct);
    }

    /// <summary>Reads the session transcript plus every subagent transcript beside it. A subagent's
    /// real usage — with its input/output/cache split and its own model — is only in its own file;
    /// the parent's completion notice reports just the subagent's final context size. A usage-only
    /// marker reads every file from line 0 with a throwaway cursor: the live cursors belong to live
    /// capture, which it must neither skip nor move.</summary>
    private async Task<List<TranscriptCapture>> CaptureTranscriptsAsync(
        string sessionId, ConversationMarker marker, CancellationToken ct)
    {
        async Task<TranscriptCapture> CaptureAsync(string cursorKey, string path, bool asSubagent)
        {
            var cursor = marker.UsageOnly ? 0 : await _cursorStore.GetAsync(cursorKey, ct);
            return new(cursorKey, await _conversationCapture.CaptureFromFileAsync(path, cursor, asSubagent, ct));
        }

        var captures = new List<TranscriptCapture> { await CaptureAsync(sessionId, marker.TranscriptPath, false) };
        foreach (var subagentPath in _conversationCapture.FindSubagentTranscripts(marker.TranscriptPath))
        {
            var cursorKey = $"{sessionId}/{Path.GetFileNameWithoutExtension(subagentPath)}";
            captures.Add(await CaptureAsync(cursorKey, subagentPath, true));
        }

        return captures;
    }

    private static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;

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
        var repoPath = await SessionMappings.FindRepoPathAsync(_config.OverwatchDir, sessionId);

        if (repoPath is not null)
        {
            var written = await _gitFallback.WriteFallbackAsync(sessionId, allUnshipped, repoPath, ct);
            if (written)
            {
                await _gitFallback.AddGitTrailerAsync(sessionId, repoPath, ct);
                await _queue.MarkShippedAsync(Ids(allUnshipped));
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
        FallbackEventImporter.ImportAsync(_config.OverwatchDir, _queue, _logger, TimeProvider.System);
}
