using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DeveloperOverwatch.Agent.Tests.Services;

public sealed class EventQueueRetentionTests : IDisposable
{
    // Far from the real date, so purging by the system clock instead of the injected one fails.
    private static readonly DateTime Now = new(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"ow-retention-{Guid.NewGuid():N}.db");
    private readonly EventQueue _sut;

    public EventQueueRetentionTests()
    {
        _sut = new EventQueue(_dbPath, new MutableTimeProvider(new DateTimeOffset(Now)));
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

    private Task EnqueueAsync(string id, DateTime createdAt, bool shipped) =>
        _sut.EnqueueAsync(new QueuedEvent
        {
            Id = id,
            SessionId = "s1",
            EventType = EventType.ToolExecution,
            PayloadJson = "{}",
            CreatedAt = createdAt,
            Shipped = shipped
        });

    private async Task<List<string>> AllIdsAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM events ORDER BY id";
        var ids = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetString(0));
        return ids;
    }

    // A pilot machine that is offline forever must not grow its queue forever.
    [Fact]
    public async Task PurgeUnshipped_RemovesOnlyUnshippedEventsOlderThanRetention()
    {
        await EnqueueAsync("unshipped-old", Now.AddDays(-31), shipped: false);
        await EnqueueAsync("unshipped-recent", Now.AddDays(-29), shipped: false);
        await EnqueueAsync("shipped-recent", Now.AddHours(-1), shipped: true);

        await _sut.PurgeUnshippedAsync(TimeSpan.FromDays(30));

        (await AllIdsAsync()).Should().Equal("shipped-recent", "unshipped-recent");
    }

    [Fact]
    public async Task PurgeShipped_KeepsTheDayLongWindow_ByInjectedTime()
    {
        await EnqueueAsync("shipped-old", Now.AddHours(-25), shipped: true);
        await EnqueueAsync("shipped-recent", Now.AddHours(-23), shipped: true);
        await EnqueueAsync("unshipped-old", Now.AddDays(-2), shipped: false);

        await _sut.PurgeShippedAsync(TimeSpan.FromHours(24));

        (await AllIdsAsync()).Should().Equal("shipped-recent", "unshipped-old");
    }

    // A session's access is what keeps its later items here, or lets them ship from its floor; it must outlive
    // every purge of the starts that decided it.
    [Fact]
    public async Task SessionAccess_OutlivesBothPurgesOfTheStartsThatDecidedIt()
    {
        var openFrom = Now.AddDays(-29);
        await _sut.MarkClosedAsync(await EnqueueStartAsync("closing-start", "closed", Now.AddDays(-29)));
        await _sut.MarkOpenedAsync(await EnqueueStartAsync("opening-start", "open", openFrom), openFrom);

        await _sut.PurgeShippedAsync(TimeSpan.FromHours(24));
        await _sut.PurgeUnshippedAsync(TimeSpan.FromDays(28));

        (await AllIdsAsync()).Should().BeEmpty();
        (await _sut.GetAccessAsync("closed")).Should().Be(new SessionAccess(AccessState.Closed, null));
        (await _sut.GetAccessAsync("open")).Should().Be(new SessionAccess(AccessState.Open, openFrom));
    }

    private async Task<QueuedEvent> EnqueueStartAsync(string id, string sessionId, DateTime createdAt)
    {
        var start = new QueuedEvent
        {
            Id = id, SessionId = sessionId, EventType = EventType.SessionStart, PayloadJson = "{}", CreatedAt = createdAt
        };
        await _sut.EnqueueAsync(start);
        return start;
    }
}
