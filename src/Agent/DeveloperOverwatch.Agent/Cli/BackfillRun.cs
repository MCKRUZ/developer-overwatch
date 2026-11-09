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

    private int _filesScanned;
    private int _filesTooOld;
    private int _sessions;
    private int _added;
    private int _alreadyPresent;
    private int _parseErrors;
    private int _queued;

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
            _filesScanned, _filesTooOld, _sessions, _added, _alreadyPresent, _parseErrors, _queued);
    }

    private async Task ProcessTranscriptAsync(string transcript, CancellationToken ct)
    {
        _filesScanned++;
        if (File.GetLastWriteTimeUtc(transcript) < cutoffUtc)
        {
            _filesTooOld++;
            return;
        }

        _sessions++;
        var claudeSessionId = Path.GetFileNameWithoutExtension(transcript);
        var header = await TranscriptHeader.ReadAsync(transcript, ct);
        var session = await BackfillSessionIds.ResolveAsync(config.OverwatchDir, claudeSessionId, ct);

        // Each usage row copies its project from session_project, so the project goes in first.
        var projectKey = await SessionStartPayload.ResolveProjectKeyAsync(
            JsonSerializer.Serialize(new { repoPath = header.Cwd }), services.Git, ct);
        await services.Usage.SetSessionProjectAsync(session.Id, projectKey, ct);

        var turns = await CaptureInWindowAsync(transcript, ct);
        var recorded = await services.Usage.RecordTurnsAsync(session.Id, turns, ct);
        _added += recorded.Added;
        _alreadyPresent += recorded.AlreadyPresent;

        if (Pilot && turns.Count > 0)
            await QueueForShippingAsync(transcript, claudeSessionId, session, header, ct);
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
            inWindow.AddRange(result.Turns.Where(t => t.Timestamp is { } ts && ToUtc(ts) >= cutoffUtc));
        }

        return inWindow;
    }

    /// <summary>Queues what the hooks would have: a SessionStart, then a ConversationTurn marker, so the
    /// processor ships the session through its normal path, cursors and stable turn ids included.</summary>
    private async Task QueueForShippingAsync(
        string transcript, string claudeSessionId, BackfillSession session, TranscriptHeader header, CancellationToken ct)
    {
        // A cursor means the running agent already captured this session, so it owns shipping it.
        if (await services.Cursors.GetAsync(session.Id, ct) > 0)
            return;

        // Lets a later /resume of this Claude session reuse the id rather than start a second session.
        if (!session.Mapped)
            await BackfillSessionIds.WriteMappingAsync(config.OverwatchDir, claudeSessionId, session.Id, header.Cwd, ct);

        var start = await BuildSessionStartAsync(transcript, session.Id, header, ct);
        // Exactly the payload overwatch-stop.ps1 queues.
        var marker = JsonSerializer.Serialize(new { sessionId = session.Id, transcriptPath = transcript });

        await services.Queue.EnqueueAsync(Event($"start:{claudeSessionId}", session.Id, EventType.SessionStart, start), ct);
        await services.Queue.EnqueueAsync(Event($"turns:{claudeSessionId}", session.Id, EventType.ConversationTurn, marker), ct);
        _queued++;
    }

    /// <summary>The payload overwatch-session-start.ps1 queues, with the repository and start time read
    /// from the transcript rather than the live shell.</summary>
    private async Task<string> BuildSessionStartAsync(
        string transcript, string sessionId, TranscriptHeader header, CancellationToken ct)
    {
        var email = await services.Git.GetConfigValueAsync(header.Cwd, "user.email", ct);
        var name = await services.Git.GetConfigValueAsync(header.Cwd, "user.name", ct);
        var startedAt = header.StartedAt ?? File.GetLastWriteTimeUtc(transcript);

        return JsonSerializer.Serialize(new
        {
            sessionId,
            developerEmail = email ?? UnknownDeveloperEmail,
            developerName = name,
            repoPath = header.Cwd,
            title = TranscriptHeader.ReadCustomTitle(transcript),
            startedAt = startedAt.ToString("o", CultureInfo.InvariantCulture),
        });
    }

    /// <summary>Ids derive from the Claude session, so running backfill again before the processor has
    /// shipped the first copy queues nothing new (the queue ignores a duplicate id).</summary>
    private QueuedEvent Event(string kind, string sessionId, EventType type, string payload) => new()
    {
        Id = StableTurnId.From($"overwatch:backfill:{kind}"),
        SessionId = sessionId,
        EventType = type,
        PayloadJson = payload,
        CreatedAt = nowUtc,
    };

    private static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
}
