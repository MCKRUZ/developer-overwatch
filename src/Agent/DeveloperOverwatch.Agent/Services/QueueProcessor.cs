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
    private readonly IUsageStore _usageStore;
    private readonly AgentConfig _config;
    private readonly IAgentModeSource _modeSource;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<QueueProcessor> _logger;
    private readonly SessionShipper _sessions;

    private int _consecutiveFailures;

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

        var conversations = new ConversationShipper(api, redactor, conversationCapture, cursorStore, usageStore, logger);
        _sessions = new SessionShipper(queue, api, redactor, gitRemote, usageStore, conversations, logger);
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

        var anyTransient = false;
        foreach (var sessionGroup in batch.GroupBy(e => e.SessionId))
        {
            var sessionId = sessionGroup.Key;
            var local = sessionGroup.Where(HandlesLocally).ToList();
            var toShip = sessionGroup.Where(e => !HandlesLocally(e)).ToList();

            // Local first, so a start handled here is already marked when the shipped items ask for it.
            if (local.Count > 0)
                await _sessions.HandleLocallyAsync(sessionId, local, ct);
            // A transient failure leaves the events queued; the 30-day unshipped purge bounds the wait.
            if (toShip.Count > 0 && await _sessions.ShipAsync(sessionId, toShip, ct))
                anyTransient = true;
        }

        // Once per poll, so one failing session never delays the others in the batch.
        if (!anyTransient)
        {
            _consecutiveFailures = 0;
            return;
        }

        _consecutiveFailures++;
        await ApplyBackoffAsync(ct);
    }

    /// <summary>The one place that decides, per event, whether it is handled on this machine or shipped.
    /// Local mode handles every event here and sends nothing: Pilot capture starts at enrolment, and
    /// Local-era data leaves the machine only through an explicit usage-only backfill.</summary>
    private bool HandlesLocally(QueuedEvent evt) => _config.Mode == AgentMode.Local;

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
