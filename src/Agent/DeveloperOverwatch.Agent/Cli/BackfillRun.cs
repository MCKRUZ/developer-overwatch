using System.Globalization;
using System.Text.Json;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;

namespace DeveloperOverwatch.Agent.Cli;

/// <summary>
/// One pass of <c>overwatch-agent backfill</c>. Transcripts are read one file at a time, streaming, so a
/// long history never sits in memory at once.
/// </summary>
internal sealed class BackfillRun(BackfillServices services, AgentConfig config, DateTime nowUtc, DateTime cutoffUtc)
{
    // What overwatch-session-start.ps1 sends when git has no user.email.
    private const string UnknownDeveloperEmail = "unknown@localhost";

    // A session active this recently may be the one running backfill, so it is not marked ended.
    private static readonly TimeSpan StillActiveWindow = TimeSpan.FromMinutes(30);

    private readonly Dictionary<string, RepoInfo> _repos = new(StringComparer.Ordinal);

    private int _filesScanned;
    private int _filesTooOld;
    private int _filesUnreadable;
    private int _sessions;
    private int _added;
    private int _alreadyPresent;
    private int _parseErrors;
    private int _queued;
    private int _stillActive;

    private bool Pilot => config.Mode == AgentMode.Pilot;

    public async Task<BackfillSummary> ExecuteAsync(CancellationToken ct)
    {
        await services.Usage.InitializeAsync(ct);
        if (Pilot)
            await services.Queue.InitializeAsync(ct);

        foreach (var projectDir in Directory.EnumerateDirectories(services.ProjectsDir))
        {
            foreach (var transcript in Directory.EnumerateFiles(projectDir, "*.jsonl"))
                await ProcessTranscriptAsync(transcript, ct);
        }

        return new BackfillSummary(
            _filesScanned, _filesTooOld, _filesUnreadable, _sessions, _added, _alreadyPresent, _parseErrors, _queued,
            _stillActive);
    }

    private async Task ProcessTranscriptAsync(string transcript, CancellationToken ct)
    {
        _filesScanned++;
        if (File.GetLastWriteTimeUtc(transcript) < cutoffUtc)
        {
            _filesTooOld++;
            return;
        }

        try
        {
            await ProcessRecentTranscriptAsync(transcript, ct);
        }
        catch (UnauthorizedAccessException)
        {
            // A transcript or mapping file this user may not read skips that session, not the whole run.
            _filesUnreadable++;
        }
    }

    private async Task ProcessRecentTranscriptAsync(string transcript, CancellationToken ct)
    {
        var claudeSessionId = Path.GetFileNameWithoutExtension(transcript);
        var header = await TranscriptHeader.ReadAsync(transcript, ct);
        var session = await BackfillSessionIds.ResolveAsync(config.OverwatchDir, claudeSessionId, ct);
        var repo = await ResolveRepoAsync(header.Cwd, ct);

        // Each usage row copies its project from session_project, so the project goes in first.
        await services.Usage.SetSessionProjectAsync(session.Id, repo.ProjectKey, ct);

        var turns = await CaptureInWindowAsync(transcript, ct);
        var recorded = await services.Usage.RecordTurnsAsync(session.Id, turns, ct);
        _sessions++;
        _added += recorded.Added;
        _alreadyPresent += recorded.AlreadyPresent;

        if (Pilot && turns.Count > 0)
            await QueueForShippingAsync(transcript, claudeSessionId, session, header, repo, ct);
    }

    /// <param name="Root">The repository root, as the session-start hook sends it; null for a folder in
    /// no repository, and then no project key is sent either, as the live hook does.</param>
    private sealed record RepoInfo(string? Root, string? ProjectKey, string? Email, string? Name);

    /// <summary>Asks git once per working folder per run: many sessions share a folder.</summary>
    private async Task<RepoInfo> ResolveRepoAsync(string? cwd, CancellationToken ct)
    {
        if (_repos.TryGetValue(cwd ?? "", out var cached))
            return cached;

        var root = cwd is null ? null : await services.Git.GetTopLevelAsync(cwd, ct);
        var projectKey = root is null
            ? null
            : await SessionStartPayload.ResolveProjectKeyAsync(JsonSerializer.Serialize(new { repoPath = root }), services.Git, ct);
        var info = new RepoInfo(
            root,
            projectKey,
            await services.Git.GetConfigValueAsync(cwd, "user.email", ct),
            await services.Git.GetConfigValueAsync(cwd, "user.name", ct));

        _repos[cwd ?? ""] = info;
        return info;
    }

    private async Task<List<CapturedTurn>> CaptureInWindowAsync(string transcript, CancellationToken ct)
    {
        var sources = services.Capture.FindSubagentTranscripts(transcript)
            .Select(path => (Path: path, AsSubagent: true))
            .Prepend((Path: transcript, AsSubagent: false));

        var inWindow = new List<CapturedTurn>();
        foreach (var (path, asSubagent) in sources)
        {
            var result = await services.Capture.CaptureFromFileAsync(path, 0, asSubagent, ct);
            _parseErrors += result.ParseErrors;
            inWindow.AddRange(result.Turns.Where(t => t.Timestamp is { } ts && UtcInstant.Utc(ts) >= cutoffUtc));
        }

        return inWindow;
    }

    /// <summary>Queues what the hooks would have: a SessionStart, a ConversationTurn marker and a SessionEnd,
    /// so the processor ships the session through its normal path with stable turn ids. The marker is
    /// usage-only and starts at the cutoff, so no conversation text and nothing outside --days leaves the
    /// machine. Live capture may already have shipped some of these turns; the server skips ids it stores.
    /// A session active within the last 30 minutes gets no SessionEnd: it may be live, even the session
    /// running backfill, and its own SessionEnd hook closes it.</summary>
    private async Task QueueForShippingAsync(
        string transcript, string claudeSessionId, BackfillSession session, TranscriptHeader header, RepoInfo repo,
        CancellationToken ct)
    {
        // Lets a later /resume of this Claude session reuse the id rather than start a second session.
        if (!session.Mapped)
            await BackfillSessionIds.WriteMappingAsync(config.OverwatchDir, claudeSessionId, session.Id, repo.Root, ct);

        var lastWriteUtc = File.GetLastWriteTimeUtc(transcript);
        var lastActiveUtc = UtcInstant.Utc(await TranscriptHeader.ReadLastTimestampAsync(transcript, ct) ?? lastWriteUtc);
        var lastActiveAt = Iso(lastActiveUtc);
        var stillActive = nowUtc - lastActiveUtc < StillActiveWindow || nowUtc - lastWriteUtc < StillActiveWindow;
        // The processor reads the marker as "idle", dated by "at": when the session was last active.
        var marker = JsonSerializer.Serialize(new
        {
            sessionId = session.Id,
            transcriptPath = transcript,
            at = lastActiveAt,
            usageOnly = true,
            isBackfill = true,
            from = Iso(cutoffUtc),
        });
        // The payload overwatch-session-end.ps1 queues, dated when the session was last active.
        // isBackfill lets the end ship through the processor's backfill lane, as the start and marker do.
        var end = JsonSerializer.Serialize(new { sessionId = session.Id, endedAt = lastActiveAt, isBackfill = true });

        await services.Queue.EnqueueAsync(
            Event($"start:{claudeSessionId}", session.Id, EventType.SessionStart, BuildSessionStart(transcript, session.Id, header, repo), 0), ct);
        await services.Queue.EnqueueAsync(Event($"turns:{claudeSessionId}", session.Id, EventType.ConversationTurn, marker, 1), ct);
        if (stillActive)
            _stillActive++;
        else
            await services.Queue.EnqueueAsync(Event($"end:{claudeSessionId}", session.Id, EventType.SessionEnd, end, 2), ct);
        _queued++;
    }

    /// <summary>The payload overwatch-session-start.ps1 queues, with the repository and start time read
    /// from the transcript rather than the live shell. <c>isBackfill</c> tells the server to fill in only
    /// what it is missing and never reopen a session that has ended.</summary>
    private static string BuildSessionStart(string transcript, string sessionId, TranscriptHeader header, RepoInfo repo) =>
        JsonSerializer.Serialize(new
        {
            sessionId,
            developerEmail = repo.Email ?? UnknownDeveloperEmail,
            developerName = repo.Name,
            repoPath = repo.Root,
            startedAt = Iso(header.StartedAt ?? File.GetLastWriteTimeUtc(transcript)),
            isBackfill = true,
        });

    /// <summary>Ids derive from the Claude session, so running backfill again before the processor has
    /// shipped the first copy queues nothing new (the queue ignores a duplicate id). CreatedAt is now, so
    /// retention treats the events as new, and <paramref name="order"/> ticks later, so start, marker and
    /// end dequeue in that order whatever their rowids.</summary>
    private QueuedEvent Event(string kind, string sessionId, EventType type, string payload, int order) => new()
    {
        Id = StableTurnId.From($"overwatch:backfill:{kind}"),
        SessionId = sessionId,
        EventType = type,
        PayloadJson = payload,
        CreatedAt = nowUtc.AddTicks(order),
    };

    private static string Iso(DateTime value) =>
        UtcInstant.Utc(value).ToString("o", CultureInfo.InvariantCulture);
}
