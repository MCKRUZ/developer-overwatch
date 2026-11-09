using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DeveloperOverwatch.Agent.Tests.Services;

public sealed class UsageStoreTests : IDisposable
{
    // Far from the real date, so a store that read the system clock instead of the injected one
    // would purge the wrong rows and fail these tests.
    private static readonly DateTime Now = new(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"ow-usage-{Guid.NewGuid():N}.db");
    private readonly MutableTimeProvider _time = new(new DateTimeOffset(Now));
    private readonly UsageStore _sut;

    public UsageStoreTests()
    {
        _sut = new UsageStore(_dbPath, _time);
        _sut.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _sut.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath) + "*"))
        {
            try { File.Delete(file); } catch (IOException) { /* best-effort cleanup */ }
        }
    }

    private static CapturedTurn Turn(
        string sourceId,
        string role = "assistant",
        DateTime? timestamp = null,
        int? input = 10,
        int? output = 20,
        int? cacheCreate = 30,
        int? cacheRead = 40,
        string? subagent = null,
        bool noTimestamp = false) =>
        new(sourceId, role, "prompt or response text that must never be stored",
            noTimestamp ? null : timestamp ?? Now.AddMinutes(-5),
            input, output, cacheCreate, cacheRead, "claude-opus-5-5", "main", subagent);

    private Task<IReadOnlyList<Models.UsageRecord>> AllAsync() =>
        _sut.GetUsageAsync(Now.AddDays(-60), Now.AddDays(1));

    [Fact]
    public async Task RecordTurns_StoresOneRowPerResponse_WithTheSessionsProject()
    {
        await _sut.SetSessionProjectAsync("s1", "github.com/acme/widget");

        await _sut.RecordTurnsAsync("s1", [Turn("a1"), Turn("sub1", role: "subagent", subagent: "explorer")]);

        var rows = await AllAsync();
        rows.Should().HaveCount(2);
        var main = rows.Single(r => r.SourceId == "a1");
        main.SessionId.Should().Be("s1");
        main.Timestamp.Should().Be(Now.AddMinutes(-5));
        main.Timestamp.Kind.Should().Be(DateTimeKind.Utc);
        main.Model.Should().Be("claude-opus-5-5");
        main.InputTokens.Should().Be(10);
        main.OutputTokens.Should().Be(20);
        main.CacheCreationTokens.Should().Be(30);
        main.CacheReadTokens.Should().Be(40);
        main.ProjectKey.Should().Be("github.com/acme/widget");
        main.GitBranch.Should().Be("main");
        rows.Single(r => r.SourceId == "sub1").SubagentName.Should().Be("explorer");
    }

    // The table is the privacy boundary: it has no column that could hold prompt or response text.
    [Fact]
    public async Task UsageTable_HasOnlyUsageColumns()
    {
        await using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM pragma_table_info('usage')";
        var columns = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                columns.Add(reader.GetString(0));

        columns.Should().BeEquivalentTo(
            "source_id", "session_id", "timestamp", "model", "input_tokens", "output_tokens",
            "cache_read_tokens", "cache_creation_tokens", "project_key", "git_branch", "subagent_name");
    }

    // A failed send re-captures the same turns on the next tick; they must not count twice.
    [Fact]
    public async Task RecordTurns_SameSourceIdTwice_CountsOnce()
    {
        await _sut.RecordTurnsAsync("s1", [Turn("a1")]);
        await _sut.RecordTurnsAsync("s1", [Turn("a1")]);

        (await AllAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task RecordTurns_SkipsUserTurnsAndTurnsWithoutUsage()
    {
        await _sut.RecordTurnsAsync("s1",
        [
            Turn("u1", role: "user"),
            Turn("a2", input: null, output: null, cacheCreate: null, cacheRead: null),
        ]);

        (await AllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task RecordTurns_NoKnownProject_LeavesProjectKeyNull()
    {
        await _sut.RecordTurnsAsync("s1", [Turn("a1")]);

        (await AllAsync()).Single().ProjectKey.Should().BeNull();
    }

    [Fact]
    public async Task RecordTurns_NoTimestamp_UsesInjectedNow()
    {
        await _sut.RecordTurnsAsync("s1", [Turn("a1", noTimestamp: true)]);

        (await AllAsync()).Single().Timestamp.Should().Be(Now);
    }

    [Fact]
    public async Task GetUsage_ReturnsRowsInHalfOpenRange()
    {
        await _sut.RecordTurnsAsync("s1",
        [
            Turn("before", timestamp: Now.AddHours(-2)),
            Turn("start", timestamp: Now.AddHours(-1)),
            Turn("end", timestamp: Now),
        ]);

        var rows = await _sut.GetUsageAsync(Now.AddHours(-1), Now);

        rows.Select(r => r.SourceId).Should().Equal("start");
    }

    [Fact]
    public async Task Purge_RemovesUsageOlderThanRetention()
    {
        await _sut.RecordTurnsAsync("s1",
        [
            Turn("old", timestamp: Now.AddDays(-31)),
            Turn("recent", timestamp: Now.AddDays(-29)),
        ]);

        await _sut.PurgeAsync(TimeSpan.FromDays(30));

        (await AllAsync()).Select(r => r.SourceId).Should().Equal("recent");
    }

    [Fact]
    public async Task Purge_RemovesStaleSessionProjects()
    {
        await _sut.SetSessionProjectAsync("s1", "github.com/acme/widget");
        _time.Now = _time.Now.AddDays(31);

        await _sut.PurgeAsync(TimeSpan.FromDays(30));
        await _sut.RecordTurnsAsync("s1", [Turn("a1", timestamp: Now.AddDays(31))]);

        var rows = await _sut.GetUsageAsync(Now, Now.AddDays(32));
        rows.Single().ProjectKey.Should().BeNull();
    }
}
