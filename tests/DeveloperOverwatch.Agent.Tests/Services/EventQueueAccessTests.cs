using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>session_access is the one record of whether a session may ship and from when. A session that ships
/// from the wrong instant leaks turns from before the developer opted in, or drops ones from after.</summary>
public sealed class EventQueueAccessTests : IDisposable
{
    private static readonly DateTime X = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath;
    private readonly EventQueue _sut;

    public EventQueueAccessTests()
    {
        _dbPath = Path.GetTempFileName() + ".db";
        _sut = new EventQueue(_dbPath);
    }

    public void Dispose()
    {
        _sut.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath) + "*"))
        {
            try { File.Delete(file); } catch { /* best-effort cleanup */ }
        }
    }

    private async Task<QueuedEvent> QueueStartAsync(DateTime createdAt, string sessionId = "s1")
    {
        var start = new QueuedEvent
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = sessionId,
            EventType = EventType.SessionStart,
            PayloadJson = "{}",
            CreatedAt = createdAt
        };
        await _sut.EnqueueAsync(start);
        return start;
    }

    [Fact]
    public async Task GetAccess_NoDecision_IsNew()
    {
        await _sut.InitializeAsync();

        (await _sut.GetAccessAsync("s1")).Should().Be(new SessionAccess(AccessState.New, null));
    }

    [Fact]
    public async Task MarkOpened_NewSession_OpensFromTheGivenInstant()
    {
        await _sut.InitializeAsync();
        var start = await QueueStartAsync(X);

        var effective = await _sut.MarkOpenedAsync(start, X);

        effective.Should().Be(X);
        var access = await _sut.GetAccessAsync("s1");
        access.State.Should().Be(AccessState.Open);
        access.OpenFrom.Should().Be(X);
        access.OpenFrom!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task MarkOpened_AlreadyOpen_FirstOpeningWins()
    {
        await _sut.InitializeAsync();
        await _sut.MarkOpenedAsync(await QueueStartAsync(X), X);

        var later = X.AddHours(1);
        var effective = await _sut.MarkOpenedAsync(await QueueStartAsync(later), later);

        effective.Should().Be(X);
        (await _sut.GetAccessAsync("s1")).Should().Be(new SessionAccess(AccessState.Open, X));
    }

    [Fact]
    public async Task MarkClosed_StartQueuedAfterTheOpening_ClosesTheSession()
    {
        await _sut.InitializeAsync();
        await _sut.MarkOpenedAsync(await QueueStartAsync(X), X);

        await _sut.MarkClosedAsync(await QueueStartAsync(X.AddMinutes(5)));

        (await _sut.GetAccessAsync("s1")).Should().Be(new SessionAccess(AccessState.Closed, null));
    }

    [Fact]
    public async Task MarkClosed_StaleStartQueuedBeforeTheOpening_LeavesTheSessionOpen()
    {
        await _sut.InitializeAsync();
        var stale = await QueueStartAsync(X.AddMinutes(-5));
        await _sut.MarkOpenedAsync(await QueueStartAsync(X), X);

        await _sut.MarkClosedAsync(stale);

        (await _sut.GetAccessAsync("s1")).Should().Be(new SessionAccess(AccessState.Open, X));
    }

    [Fact]
    public async Task MarkOpened_ClosedSession_OpensFromTheNewInstant()
    {
        await _sut.InitializeAsync();
        await _sut.MarkClosedAsync(await QueueStartAsync(X));

        var z = X.AddHours(2);
        var effective = await _sut.MarkOpenedAsync(await QueueStartAsync(z), z);

        effective.Should().Be(z);
        (await _sut.GetAccessAsync("s1")).Should().Be(new SessionAccess(AccessState.Open, z));
    }

    [Fact]
    public async Task MarkClosed_NewSession_ClosesIt()
    {
        await _sut.InitializeAsync();

        await _sut.MarkClosedAsync(await QueueStartAsync(X));

        (await _sut.GetAccessAsync("s1")).State.Should().Be(AccessState.Closed);
    }

    [Fact]
    public async Task MarkOpenedAndClosed_SettleTheStart_SoItIsNeverDequeuedAgain()
    {
        await _sut.InitializeAsync();
        var opened = await QueueStartAsync(X);
        var closed = await QueueStartAsync(X, sessionId: "s2");
        var pending = await QueueStartAsync(X, sessionId: "s3");

        await _sut.MarkOpenedAsync(opened, X);
        await _sut.MarkClosedAsync(closed);

        var batch = await _sut.DequeueBatchAsync(10);
        batch.Select(e => e.Id).Should().Equal(pending.Id);
        (await _sut.GetUnshippedCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task MarkOpened_LocalInstant_IsStoredAsTheSameUtcInstant()
    {
        await _sut.InitializeAsync();
        var local = X.ToLocalTime();

        var effective = await _sut.MarkOpenedAsync(await QueueStartAsync(X), local);

        effective.Kind.Should().Be(DateTimeKind.Utc);
        effective.Ticks.Should().Be(X.Ticks);
    }
}
