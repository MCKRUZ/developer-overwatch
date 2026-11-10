using System.Globalization;
using System.Text.Json;
using DeveloperOverwatch.Agent.Cli;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace DeveloperOverwatch.Agent.Tests.Cli;

/// <summary>Runs backfill over a hand-built Claude projects folder in a temp dir, with the real usage store,
/// queue, cursor store and transcript parser. No real transcript is ever read.</summary>
public sealed class BackfillCommandTests : IDisposable
{
    private const string SessionA = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string SessionB = "bbbbbbbb-0000-0000-0000-000000000002";
    private const string SessionOld = "cccccccc-0000-0000-0000-000000000003";
    private const string MappedId = "11111111-2222-3333-4444-555555555555";
    private const string RemoteA = "https://github.com/Acme/Widget.git";

    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeProvider Time = new ZonedTimeProvider(Now, TimeZoneInfo.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-backfill-{Guid.NewGuid():N}");
    private readonly string _projects;
    private readonly string _overwatch;
    private readonly string _cwdA;
    private readonly string _cwdB;
    private readonly string _transcriptA;
    private readonly UsageStore _usage;
    private readonly EventQueue _queue;
    private readonly IGitReader _git = Substitute.For<IGitReader>();

    public BackfillCommandTests()
    {
        _projects = Path.Combine(_dir, "projects");
        _overwatch = Path.Combine(_dir, "overwatch");
        _cwdA = Path.Combine(_dir, "repo-a");
        _cwdB = Path.Combine(_dir, "repo-b");
        _transcriptA = Path.Combine(_projects, "C--repo-a", $"{SessionA}.jsonl");
        Directory.CreateDirectory(_overwatch);

        var dbPath = Path.Combine(_overwatch, "queue.db");
        _usage = new UsageStore(dbPath, Time);
        _queue = new EventQueue(dbPath, Time);
        _queue.InitializeAsync().GetAwaiter().GetResult();

        _git.GetOriginUrlAsync(_cwdA, Arg.Any<CancellationToken>()).Returns(RemoteA);
        _git.GetTopLevelAsync(_cwdA, Arg.Any<CancellationToken>()).Returns(_cwdA);
        _git.GetTopLevelAsync(_cwdB, Arg.Any<CancellationToken>()).Returns(_cwdB);
        _git.GetConfigValueAsync(Arg.Any<string?>(), "user.email", Arg.Any<CancellationToken>()).Returns("dev@example.com");
        WriteFixture();
    }

    public void Dispose()
    {
        _queue.Dispose();
        _usage.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task Local_RecordsRecentResponsesWithProjectAndSubagent_AndReportsCounts()
    {
        var (exit, output) = await RunAsync(AgentMode.Local);

        exit.Should().Be(0);
        output.Should().MatchRegex(@"Transcripts scanned\s+3")
            .And.MatchRegex(@"Skipped, too old\s+1")
            .And.MatchRegex(@"Sessions\s+2")
            .And.Contain("4 new, 0 already present")
            .And.MatchRegex(@"Parse errors\s+1")
            .And.NotContain("Queued for shipping");

        var rows = await AllRowsAsync();
        rows.Should().HaveCount(4);
        rows.Count(r => r.ProjectKey == ProjectKey.From(RemoteA, _cwdA)).Should().Be(3);
        rows.Count(r => r.ProjectKey == ProjectKey.From(null, _cwdB)).Should().Be(1);
        rows.Where(r => r.SubagentName is not null).Select(r => r.SubagentName).Should().Equal("Explore");
        (await _queue.GetUnshippedCountAsync()).Should().Be(0, "local mode never queues anything for shipping");
    }

    [Fact]
    public async Task RunningTwice_RecordsNothingNew()
    {
        await RunAsync(AgentMode.Local);

        var (_, output) = await RunAsync(AgentMode.Local);

        output.Should().Contain("0 new, 4 already present");
        (await AllRowsAsync()).Should().HaveCount(4);
    }

    [Fact]
    public async Task DaysOption_WidensTheWindow_ButStillSkipsOlderFiles()
    {
        var (_, output) = await RunAsync(AgentMode.Local, "--days", "50");

        output.Should().Contain("5 new, 0 already present").And.MatchRegex(@"Skipped, too old\s+1");
        (await AllRowsAsync()).Should().HaveCount(5);
    }

    // Backfill sends usage only: a /rename'd title stays on this machine.
    [Fact]
    public async Task Pilot_RenamedSession_QueuesAStartWithNoTitle()
    {
        WriteMapping(SessionA, MappedId);
        var sidecar = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(_transcriptA)!, SessionA));
        File.WriteAllText(Path.Combine(sidecar.FullName, "custom-title.json"), "{\"customTitle\":\"renamed\"}");

        await RunAsync(AgentMode.Pilot);

        var start = (await _queue.DequeueBatchAsync(100)).Single(e => e.SessionId == MappedId && e.EventType == EventType.SessionStart);
        start.PayloadJson.Should().NotContain("renamed");
    }

    [Fact]
    public async Task Pilot_MappedSession_QueuesSessionStartAndMarkerUnderTheMappedId()
    {
        WriteMapping(SessionA, MappedId);

        var (_, output) = await RunAsync(AgentMode.Pilot);

        output.Should().MatchRegex(@"Queued for shipping\s+2");
        var events = (await _queue.DequeueBatchAsync(100)).Where(e => e.SessionId == MappedId).ToList();
        events.Select(e => e.EventType).Should().Equal(EventType.SessionStart, EventType.ConversationTurn, EventType.SessionEnd);
        events.Select(e => e.CreatedAt).Should().Equal([Now, Now.AddTicks(1), Now.AddTicks(2)],
            "start, marker, end must dequeue in that order whatever their rowids");

        using var start = JsonDocument.Parse(events[0].PayloadJson);
        start.RootElement.GetProperty("isBackfill").GetBoolean().Should().BeTrue(
            "the server must only fill in a session it has, never reopen an ended one");
        start.RootElement.GetProperty("sessionId").GetString().Should().Be(MappedId);
        start.RootElement.GetProperty("developerEmail").GetString().Should().Be("dev@example.com");
        start.RootElement.GetProperty("repoPath").GetString().Should().Be(_cwdA);
        start.RootElement.GetProperty("startedAt").GetDateTime().ToUniversalTime().Should().Be(Now.AddDays(-40));

        using var marker = JsonDocument.Parse(events[1].PayloadJson);
        marker.RootElement.EnumerateObject().Select(p => p.Name).Should()
            .Equal("sessionId", "transcriptPath", "at", "usageOnly", "isBackfill", "from");
        marker.RootElement.GetProperty("isBackfill").GetBoolean().Should().BeTrue(
            "the explicit backfill opt-in is what lets pre-enrolment usage ship");
        marker.RootElement.GetProperty("sessionId").GetString().Should().Be(MappedId);
        marker.RootElement.GetProperty("transcriptPath").GetString().Should().Be(_transcriptA);
        marker.RootElement.GetProperty("at").GetDateTime().ToUniversalTime().Should().Be(Now.AddDays(-1),
            "the marker means idle, and the session went idle at its last transcript line, not at backfill time");
        marker.RootElement.GetProperty("usageOnly").GetBoolean().Should().BeTrue("backfill never ships conversation text");
        marker.RootElement.GetProperty("from").GetDateTime().ToUniversalTime().Should()
            .BeAfter(Now.AddDays(-40)).And.BeOnOrBefore(Now.AddDays(-2), "only the --days window ships");

        using var end = JsonDocument.Parse(events[2].PayloadJson);
        end.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("sessionId", "endedAt", "isBackfill");
        end.RootElement.GetProperty("isBackfill").GetBoolean().Should().BeTrue(
            "the end must ship through the backfill lane, or a session no start opened never completes");
        end.RootElement.GetProperty("sessionId").GetString().Should().Be(MappedId);
        end.RootElement.GetProperty("endedAt").GetDateTime().ToUniversalTime().Should().Be(Now.AddDays(-1));
    }

    [Fact]
    public async Task Pilot_UnmappedSession_UsesTheSameDerivedIdEveryRun_AndWritesTheMapping()
    {
        await RunAsync(AgentMode.Pilot);
        await RunAsync(AgentMode.Pilot);

        var events = await _queue.DequeueBatchAsync(100);
        events.Should().HaveCount(6, "a second run before shipping queues no duplicates");
        var idsB = events.Where(e => e.PayloadJson.Contains(SessionB, StringComparison.Ordinal)
                                     || IsSessionStartFor(e, _cwdB))
            .Select(e => e.SessionId).Distinct().ToList();
        idsB.Should().ContainSingle();
        Guid.TryParse(idsB[0], out var derived).Should().BeTrue();
        derived.ToString().Should().NotBe(SessionB, "the Overwatch id is derived from, not equal to, the Claude id");

        using var mapping = JsonDocument.Parse(await File.ReadAllTextAsync(MappingPath(SessionB)));
        mapping.RootElement.GetProperty("ourSessionId").GetString().Should().Be(idsB[0]);
        mapping.RootElement.GetProperty("repoPath").GetString().Should().Be(_cwdB);
    }

    [Fact]
    public async Task Pilot_SessionWithALiveCursor_IsStillQueued_AndTheCursorIsUntouched()
    {
        // A cursor can come from local mode, which shipped nothing. The server skips turn ids it already
        // stores, so overlap with live capture is safe.
        WriteMapping(SessionA, MappedId);
        var cursors = new ConversationCursorStore(_overwatch);
        await cursors.SetAsync(MappedId, 3, CancellationToken.None);

        var (_, output) = await RunAsync(AgentMode.Pilot);

        output.Should().MatchRegex(@"Queued for shipping\s+2");
        (await _queue.DequeueBatchAsync(100)).Where(e => e.SessionId == MappedId).Should().HaveCount(3);
        (await cursors.GetAsync(MappedId, CancellationToken.None)).Should().Be(3);
        (await AllRowsAsync()).Should().HaveCount(4);
    }

    // Backfill run from inside a live Claude session must not mark that session ended on the dashboard.
    [Theory]
    [InlineData(5, 60)]      // last transcript line 5 minutes ago
    [InlineData(1440, 10)]   // last line a day ago, but the file was written 10 minutes ago
    public async Task Pilot_RecentlyActiveSession_IsQueuedWithoutASessionEnd(int lastLineMinutesAgo, int writtenMinutesAgo)
    {
        WriteMapping(SessionA, MappedId);
        WriteTranscript(_transcriptA, Now.AddMinutes(-writtenMinutesAgo),
            User(SessionA, _cwdA, Now.AddDays(-2)),
            Assistant(SessionA, _cwdA, "msg-live", Now.AddMinutes(-lastLineMinutesAgo)));

        var (_, output) = await RunAsync(AgentMode.Pilot);

        output.Should().MatchRegex(@"Queued for shipping\s+2").And.MatchRegex(@"Still active, not closed\s+1");
        (await _queue.DequeueBatchAsync(100)).Where(e => e.SessionId == MappedId).Select(e => e.EventType)
            .Should().Equal([EventType.SessionStart, EventType.ConversationTurn],
                "a session active in the last 30 minutes may be live, and its own SessionEnd hook closes it");
    }

    [Fact]
    public async Task Pilot_SessionIdleLongerThanTheWindow_IsClosed()
    {
        WriteMapping(SessionA, MappedId);
        WriteTranscript(_transcriptA, Now.AddMinutes(-31),
            Assistant(SessionA, _cwdA, "msg-idle", Now.AddMinutes(-31)));

        var (_, output) = await RunAsync(AgentMode.Pilot);

        output.Should().MatchRegex(@"Still active, not closed\s+0");
        (await _queue.DequeueBatchAsync(100)).Where(e => e.SessionId == MappedId).Select(e => e.EventType)
            .Should().EndWith(EventType.SessionEnd);
    }

    // A transcript can start in a folder outside any repository, such as Documents. Like the live hook,
    // backfill then sends no repository and no project key, rather than the raw folder.
    [Fact]
    public async Task Pilot_CwdOutsideAnyRepository_SendsNoRepoPathOrProjectKey()
    {
        _git.GetTopLevelAsync(_cwdB, Arg.Any<CancellationToken>()).Returns((string?)null);

        await RunAsync(AgentMode.Pilot);

        var starts = (await _queue.DequeueBatchAsync(100)).Where(e => e.EventType == EventType.SessionStart)
            .Select(e => JsonDocument.Parse(e.PayloadJson).RootElement.GetProperty("repoPath")).ToList();
        starts.Select(r => r.ValueKind == JsonValueKind.Null ? null : r.GetString()).Should().BeEquivalentTo([_cwdA, null]);
        var rows = await AllRowsAsync();
        rows.Count(r => r.ProjectKey is null).Should().Be(1, "session B's one response has no project");
        rows.Count(r => r.ProjectKey == ProjectKey.From(RemoteA, _cwdA)).Should().Be(3);
    }

    [Theory]
    [InlineData("--days", "0")]
    [InlineData("--days", "366")]
    [InlineData("--days", "abc")]
    [InlineData("--days")]
    [InlineData("--weeks", "2")]
    public async Task InvalidArguments_ExitTwoWithHelp(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await CommandDispatcher.RunAsync(
            ["backfill", .. args], output, error, new AgentConfig { OverwatchDir = _overwatch }, Time);

        exit.Should().Be(CommandDispatcher.UsageErrorExitCode);
        error.ToString().Should().Contain("--days N").And.Contain("Usage: overwatch-agent");
        output.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task UnopenableQueueDb_ExitsOneWithAShortMessage()
    {
        var overwatch = Path.Combine(_dir, "broken-overwatch");
        Directory.CreateDirectory(Path.Combine(overwatch, "queue.db")); // a folder where the database file should be
        var error = new StringWriter();

        var exit = await CommandDispatcher.RunAsync(
            ["backfill"], new StringWriter(), error, new AgentConfig { OverwatchDir = overwatch }, Time);

        exit.Should().Be(1);
        error.ToString().Should().StartWith("Backfill failed: ").And.NotContain("   at ");
    }

    private async Task<(int Exit, string Output)> RunAsync(AgentMode mode, params string[] args)
    {
        BackfillCommand.TryParseDays(args, out var days, out _).Should().BeTrue();
        var services = new BackfillServices(
            _usage, _queue, new ConversationCursorStore(_overwatch),
            new ConversationCapture(NullLogger<ConversationCapture>.Instance), _git, _projects);
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await BackfillCommand.RunAsync(
            output, error, services, new AgentConfig { Mode = mode, OverwatchDir = _overwatch }, Time, days);

        error.ToString().Should().BeEmpty();
        return (exit, output.ToString());
    }

    private async Task<IReadOnlyList<UsageRecord>> AllRowsAsync() =>
        await _usage.GetUsageAsync(Now.AddDays(-400), Now.AddDays(1));

    private void WriteFixture()
    {
        WriteTranscript(_transcriptA, Now.AddHours(-1),
            User(SessionA, _cwdA, Now.AddDays(-40)),
            Assistant(SessionA, _cwdA, "msg-a0", Now.AddDays(-40)),
            Assistant(SessionA, _cwdA, "msg-a1", Now.AddDays(-2)),
            "{this line is not json",
            Assistant(SessionA, _cwdA, "msg-a2", Now.AddDays(-1)));

        var subagents = Path.Combine(_projects, "C--repo-a", SessionA, "subagents");
        WriteTranscript(Path.Combine(subagents, "agent-x1.jsonl"), Now.AddHours(-1),
            Assistant(SessionA, _cwdA, "msg-s1", Now.AddDays(-1)));
        File.WriteAllText(Path.Combine(subagents, "agent-x1.meta.json"), """{"agentType":"Explore"}""");

        WriteTranscript(Path.Combine(_projects, "C--repo-b", $"{SessionB}.jsonl"), Now.AddHours(-2),
            User(SessionB, _cwdB, Now.AddDays(-3)),
            Assistant(SessionB, _cwdB, "msg-b1", Now.AddDays(-3)));

        WriteTranscript(Path.Combine(_projects, "C--repo-b", $"{SessionOld}.jsonl"), Now.AddDays(-60),
            Assistant(SessionOld, _cwdB, "msg-old", Now.AddDays(-60)));
    }

    private static void WriteTranscript(string path, DateTime lastWriteUtc, params string[] lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
    }

    private void WriteMapping(string claudeSessionId, string ourSessionId)
    {
        Directory.CreateDirectory(Path.Combine(_overwatch, "sessions"));
        File.WriteAllText(MappingPath(claudeSessionId),
            JsonSerializer.Serialize(new { ourSessionId, repoPath = _cwdA }));
    }

    private string MappingPath(string claudeSessionId) => Path.Combine(_overwatch, "sessions", $"{claudeSessionId}.json");

    private static bool IsSessionStartFor(QueuedEvent e, string cwd) =>
        e.EventType == EventType.SessionStart && JsonDocument.Parse(e.PayloadJson).RootElement
            .GetProperty("repoPath").GetString() == cwd;

    private static string User(string session, string cwd, DateTime ts) =>
        $$"""{"type":"user","uuid":"{{Guid.NewGuid()}}","sessionId":"{{session}}","cwd":{{JsonSerializer.Serialize(cwd)}},"message":{"role":"user","content":"hello"},"timestamp":"{{Iso(ts)}}"}""";

    private static string Assistant(string session, string cwd, string messageId, DateTime ts) =>
        $$$"""{"type":"assistant","uuid":"{{{Guid.NewGuid()}}}","sessionId":"{{{session}}}","cwd":{{{JsonSerializer.Serialize(cwd)}}},"gitBranch":"main","message":{"id":"{{{messageId}}}","role":"assistant","model":"claude-opus-4-1","content":[{"type":"text","text":"done"}],"usage":{"input_tokens":100,"output_tokens":20,"cache_creation_input_tokens":0,"cache_read_input_tokens":0}},"timestamp":"{{{Iso(ts)}}}"}""";

    private static string Iso(DateTime ts) => ts.ToString("o", CultureInfo.InvariantCulture);
}
