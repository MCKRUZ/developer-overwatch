using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;

namespace DeveloperOverwatch.Agent.Tests.Services;

public sealed class EventQueueTests : IDisposable
{
    private readonly string _dbPath;
    private readonly EventQueue _sut;

    public EventQueueTests()
    {
        _dbPath = Path.GetTempFileName() + ".db";
        _sut = new EventQueue(_dbPath);
    }

    public void Dispose()
    {
        _sut.Dispose();

        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath) + "*"))
        {
            try { File.Delete(file); } catch { /* best-effort cleanup */ }
        }
    }

    private static QueuedEvent CreateEvent(
        string? id = null,
        string sessionId = "session-1",
        EventType eventType = EventType.ToolExecution,
        DateTime? createdAt = null,
        bool shipped = false) => new()
    {
        Id = id ?? Guid.NewGuid().ToString(),
        SessionId = sessionId,
        EventType = eventType,
        PayloadJson = "{}",
        CreatedAt = createdAt ?? DateTime.UtcNow,
        Shipped = shipped
    };

    [Fact]
    public async Task Initialize_CreatesTable()
    {
        await _sut.InitializeAsync();

        var ev = CreateEvent();
        await _sut.EnqueueAsync(ev);

        var batch = await _sut.DequeueBatchAsync(10);
        batch.Should().ContainSingle().Which.Id.Should().Be(ev.Id);
    }

    [Fact]
    public async Task Enqueue_DuplicateId_IgnoresSecond()
    {
        await _sut.InitializeAsync();

        var ev = CreateEvent(id: "dup-1");
        await _sut.EnqueueAsync(ev);
        await _sut.EnqueueAsync(ev);

        var batch = await _sut.DequeueBatchAsync(10);
        batch.Should().ContainSingle();
    }

    [Fact]
    public async Task DequeueBatch_ReturnsUnshippedInOrder()
    {
        await _sut.InitializeAsync();

        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ev1 = CreateEvent(id: "first", createdAt: baseTime);
        var ev2 = CreateEvent(id: "second", createdAt: baseTime.AddSeconds(1));
        var ev3 = CreateEvent(id: "shipped", createdAt: baseTime.AddSeconds(2), shipped: true);

        await _sut.EnqueueAsync(ev1);
        await _sut.EnqueueAsync(ev2);
        await _sut.EnqueueAsync(ev3);

        var batch = await _sut.DequeueBatchAsync(10);

        batch.Should().HaveCount(2);
        batch[0].Id.Should().Be("first");
        batch[1].Id.Should().Be("second");
    }

    // created_at is compared as text, so every row must carry the same UTC form to sort by instant.
    [Fact]
    public async Task Enqueue_LocalOrUnspecifiedTime_IsStoredAsUtc()
    {
        await _sut.InitializeAsync();

        var instant = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        await _sut.EnqueueAsync(CreateEvent(id: "local", createdAt: instant.AddSeconds(1).ToLocalTime()));
        await _sut.EnqueueAsync(CreateEvent(id: "unspecified", createdAt: DateTime.SpecifyKind(instant, DateTimeKind.Unspecified)));
        await _sut.EnqueueAsync(CreateEvent(id: "utc", createdAt: instant.AddSeconds(2)));

        var batch = await _sut.DequeueBatchAsync(10);

        batch.Select(e => e.Id).Should().Equal("unspecified", "local", "utc");
        batch.Should().AllSatisfy(e => e.CreatedAt.Kind.Should().Be(DateTimeKind.Utc));
        batch[1].CreatedAt.Should().Be(instant.AddSeconds(1));
    }

    [Fact]
    public async Task DequeueBatch_RespectsLimit()
    {
        await _sut.InitializeAsync();

        for (var i = 0; i < 5; i++)
            await _sut.EnqueueAsync(CreateEvent());

        var batch = await _sut.DequeueBatchAsync(3);
        batch.Should().HaveCount(3);
    }

    [Fact]
    public async Task MarkShipped_MarksEventsAsShipped()
    {
        await _sut.InitializeAsync();

        var ev1 = CreateEvent(id: "ship-me");
        var ev2 = CreateEvent(id: "keep-me");
        await _sut.EnqueueAsync(ev1);
        await _sut.EnqueueAsync(ev2);

        await _sut.MarkShippedAsync(["ship-me"]);

        var batch = await _sut.DequeueBatchAsync(10);
        batch.Should().ContainSingle().Which.Id.Should().Be("keep-me");
    }

    [Fact]
    public async Task IncrementRetry_IncrementsCount()
    {
        await _sut.InitializeAsync();

        var ev = CreateEvent(id: "retry-me");
        await _sut.EnqueueAsync(ev);

        await _sut.IncrementRetryAsync("retry-me");
        await _sut.IncrementRetryAsync("retry-me");

        var batch = await _sut.DequeueBatchAsync(10);
        batch.Should().ContainSingle().Which.RetryCount.Should().Be(2);
    }

    [Fact]
    public async Task GetUnshippedCount_ReturnsCorrectCount()
    {
        await _sut.InitializeAsync();

        await _sut.EnqueueAsync(CreateEvent(id: "a"));
        await _sut.EnqueueAsync(CreateEvent(id: "b"));
        await _sut.EnqueueAsync(CreateEvent(id: "c"));

        await _sut.MarkShippedAsync(["b"]);

        var count = await _sut.GetUnshippedCountAsync();
        count.Should().Be(2);
    }

    [Fact]
    public async Task PurgeShipped_DeletesOldShippedEvents()
    {
        await _sut.InitializeAsync();

        var oldTime = DateTime.UtcNow.AddDays(-10);
        var recentTime = DateTime.UtcNow;

        // Old shipped event -- should be purged
        var oldShipped = CreateEvent(id: "old-shipped", createdAt: oldTime, shipped: true);
        // Recent shipped event -- should survive
        var recentShipped = CreateEvent(id: "recent-shipped", createdAt: recentTime, shipped: true);
        // Old unshipped event -- should survive (not shipped)
        var oldUnshipped = CreateEvent(id: "old-unshipped", createdAt: oldTime);

        await _sut.EnqueueAsync(oldShipped);
        await _sut.EnqueueAsync(recentShipped);
        await _sut.EnqueueAsync(oldUnshipped);

        // Mark the shipped events as shipped in the DB (the Shipped init property sets the column on insert)
        // oldShipped and recentShipped are already inserted with shipped=true via the Shipped property

        await _sut.PurgeShippedAsync(TimeSpan.FromDays(5));

        // old-unshipped should still be dequeue-able
        var unshipped = await _sut.DequeueBatchAsync(10);
        unshipped.Should().ContainSingle().Which.Id.Should().Be("old-unshipped");

        // Only recent-shipped should remain in shipped count
        var unshippedCount = await _sut.GetUnshippedCountAsync();
        unshippedCount.Should().Be(1);
    }
}
