namespace DeveloperOverwatch.Agent.Services;

using DeveloperOverwatch.Agent.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class QueueProcessor : BackgroundService
{
    /// <summary>About a minute at the default 5 s poll: long enough to ride out a half-saved file.</summary>
    internal const int UnreadableConfigWarningPolls = 12;

    private static readonly TimeSpan ShippedRetention = TimeSpan.FromHours(24);

    // Events a machine never managed to ship are kept this long and no longer. Local usage is kept at
    // least this long, and always for the whole current month (see UsageStore.PurgeAsync).
    private static readonly TimeSpan LocalRetention = TimeSpan.FromDays(30);

    private readonly IEventQueue _queue;
    private readonly IUsageStore _usageStore;
    private readonly AgentConfig _config;
    private readonly IAgentModeSource _modeSource;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<QueueProcessor> _logger;
    private readonly SessionShipper _sessions;
    private readonly SessionBackoff _backoff;

    private int _unreadableConfigPolls;

    public QueueProcessor(
        IEventQueue queue,
        IApiClient api,
        IPrivacyRedactor redactor,
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
        _usageStore = usageStore;
        _config = config;
        _modeSource = modeSource;
        _lifetime = lifetime;
        _logger = logger;

        // The enrolment cutoff exists only in Pilot: Local mode sends nothing, so its cursors go to the end.
        var pilotSince = config.Mode == AgentMode.Pilot ? config.PilotSince : null;
        var conversations = new ConversationShipper(
            api, redactor, conversationCapture, cursorStore, usageStore, logger, config.Mode == AgentMode.Local, pilotSince);
        _sessions = new SessionShipper(queue, api, redactor, gitRemote, usageStore, conversations, logger, pilotSince);
        _backoff = new SessionBackoff(config.PollIntervalSeconds);
    }

    internal enum PollResult
    {
        Continue,
        Stop
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
                var result = await PollOnceAsync(stoppingToken);
                if (result == PollResult.Stop)
                    break;
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

    /// <summary>One poll: checks the mode, imports the hooks' fallback file, delivers one batch and purges.
    /// Exceptions propagate to the caller.</summary>
    internal async Task<PollResult> PollOnceAsync(CancellationToken ct)
    {
        var mode = ReadMode();
        if (mode is null)
            return PollResult.Continue;
        if (mode != _config.Mode)
        {
            _logger.LogWarning("Agent mode changed from {Old} to {New}; stopping so a restart picks up the new config",
                _config.Mode, mode);
            _lifetime.StopApplication();
            return PollResult.Stop;
        }

        await FallbackEventImporter.ImportAsync(_config.OverwatchDir, _queue, _logger, TimeProvider.System);
        // A session opened in an earlier enrolment may have been used in Local mode since, with no start
        // there to close it, so it is held again until the developer comes back to it.
        if (_config is { Mode: AgentMode.Pilot, PilotSince: { } pilotSince })
            await _queue.CloseOpenedBeforeAsync(pilotSince, ct);
        await ProcessBatchAsync(ct);
        await PurgeOldEventsAsync();
        return PollResult.Continue;
    }

    /// <summary>The mode is read fresh before every poll: a machine switched to Local must stop shipping
    /// now, not at the next logon. A change stops the agent; the session-start hook starts it again with
    /// the new config. Null when the config cannot be read right now (locked, half-saved): that poll ships
    /// nothing, but the agent keeps running, since the file does not say the mode changed. A config that
    /// stays unreadable is warned about once per outage.</summary>
    private AgentMode? ReadMode()
    {
        AgentMode? mode = null;
        try
        {
            mode = _modeSource.CurrentMode();
            if (mode is null)
                _logger.LogDebug("Could not parse the agent config; skipping this poll");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the agent mode; skipping this poll");
        }

        if (mode is not null)
        {
            _unreadableConfigPolls = 0;
            return mode;
        }

        if (++_unreadableConfigPolls == UnreadableConfigWarningPolls)
            _logger.LogWarning("The agent config at {Path} has been unreadable for {Count} polls in a row; nothing ships until it can be read",
                _config.ConfigPath, UnreadableConfigWarningPolls);
        return null;
    }

    /// <summary>A session left transient backs off on its own and is not dequeued until its backoff is over.</summary>
    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        var batch = await _queue.DequeueBatchAsync(_config.BatchSize, _backoff.NextPoll(), ct);
        if (batch.Count == 0)
            return;

        _logger.LogDebug("Processing batch of {Count} events", batch.Count);

        foreach (var sessionGroup in batch.GroupBy(e => e.SessionId))
        {
            var sessionId = sessionGroup.Key;
            var local = sessionGroup.Where(HandlesLocally).ToList();
            var toShip = sessionGroup.Where(e => !HandlesLocally(e)).ToList();

            // Local first, so a start handled here is already marked when the shipped items ask for it.
            if (local.Count > 0)
                await _sessions.HandleLocallyAsync(sessionId, local, ct);
            if (toShip.Count == 0)
                continue;
            // A transient failure leaves the events queued; the 30-day unshipped purge bounds the wait.
            if (await _sessions.ShipAsync(sessionId, toShip, ct))
                _backoff.Failed(sessionId);
            else
                _backoff.Succeeded(sessionId);
        }
    }

    /// <summary>The one place that decides, per event, whether it is handled on this machine or shipped.
    /// Local mode handles every event here and sends nothing. In Pilot, an event queued before enrolment
    /// (<see cref="AgentConfig.PilotSince"/>) is handled here too, so Local-era data never ships; it leaves
    /// the machine only through an explicit usage-only backfill. No PilotSince means no cutoff.</summary>
    private bool HandlesLocally(QueuedEvent evt) =>
        _config.Mode == AgentMode.Local || (_config.PilotSince is { } since && UtcInstant.Utc(evt.CreatedAt) < since);

    private async Task PurgeOldEventsAsync()
    {
        await _queue.PurgeShippedAsync(ShippedRetention);
        // A pilot machine that stays offline must not grow its queue forever.
        await _queue.PurgeUnshippedAsync(LocalRetention);
        await _usageStore.PurgeAsync(LocalRetention);
    }
}
