namespace DeveloperOverwatch.Application.Tests.Persistence;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Infrastructure.Persistence;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class SessionRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OverwatchDbContext _db;
    private readonly SessionRepository _repo;

    public SessionRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OverwatchDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new OverwatchDbContext(options);
        _db.Database.EnsureCreated();
        _repo = new SessionRepository(_db);
    }

    private static Session NewSession(
        string id, string email = "dev@test.com", string? repo = null, DateTime? startedAt = null) => new()
    {
        Id = id,
        DeveloperEmail = email,
        RepoPath = repo,
        StartedAt = startedAt ?? DateTime.UtcNow,
    };

    [Fact]
    public async Task AddAsync_ThenGetById_ReturnsSession()
    {
        await _repo.AddAsync(NewSession("s1"));
        await _db.SaveChangesAsync();

        var fetched = await _repo.GetByIdAsync("s1");

        fetched.Should().NotBeNull();
        fetched!.DeveloperEmail.Should().Be("dev@test.com");
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var fetched = await _repo.GetByIdAsync("missing");

        fetched.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_IncludesChildCollections_OrderedByIndexAndTimestamp()
    {
        await _repo.AddAsync(NewSession("s1"));
        await _repo.AddTurnsAsync(new[]
        {
            new ConversationTurn { Id = "t2", SessionId = "s1", TurnIndex = 2, Role = "user", Content = "second", Timestamp = DateTime.UtcNow },
            new ConversationTurn { Id = "t1", SessionId = "s1", TurnIndex = 1, Role = "user", Content = "first", Timestamp = DateTime.UtcNow },
        });
        await _repo.AddToolExecutionsAsync(new[]
        {
            new ToolExecution { Id = "x2", SessionId = "s1", ToolName = "Edit", Timestamp = DateTime.UtcNow.AddMinutes(1) },
            new ToolExecution { Id = "x1", SessionId = "s1", ToolName = "Read", Timestamp = DateTime.UtcNow },
        });
        await _repo.AddFileChangesAsync(new[]
        {
            new FileChange { Id = "f1", SessionId = "s1", FilePath = "a.cs", ChangeType = ChangeType.Modified },
        });
        await _db.SaveChangesAsync();

        // Detach tracked entities so the ordered Include executes against the DB
        // rather than returning navigations from the identity map in insertion order.
        _db.ChangeTracker.Clear();

        var fetched = await _repo.GetByIdAsync("s1");

        fetched.Should().NotBeNull();
        fetched!.ConversationTurns.Select(t => t.Id).Should().Equal("t1", "t2");
        fetched.ToolExecutions.Select(t => t.Id).Should().Equal("x1", "x2");
        fetched.FileChanges.Should().ContainSingle().Which.FilePath.Should().Be("a.cs");
    }

    [Fact]
    public async Task GetAllAsync_NoFilter_ReturnsAllOrderedByStartedAtDescending()
    {
        await _repo.AddAsync(NewSession("old", startedAt: DateTime.UtcNow.AddDays(-2)));
        await _repo.AddAsync(NewSession("new", startedAt: DateTime.UtcNow));
        await _db.SaveChangesAsync();

        var all = await _repo.GetAllAsync();

        all.Select(s => s.Id).Should().Equal("new", "old");
    }

    [Fact]
    public async Task GetAllAsync_FilterByDeveloperEmail_ReturnsMatchingOnly()
    {
        await _repo.AddAsync(NewSession("s1", email: "alice@test.com"));
        await _repo.AddAsync(NewSession("s2", email: "bob@test.com"));
        await _db.SaveChangesAsync();

        var result = await _repo.GetAllAsync(developerEmail: "alice@test.com");

        result.Should().ContainSingle().Which.Id.Should().Be("s1");
    }

    [Fact]
    public async Task GetAllAsync_FilterByRepo_ReturnsMatchingOnly()
    {
        await _repo.AddAsync(NewSession("s1", repo: "/repo/a"));
        await _repo.AddAsync(NewSession("s2", repo: "/repo/b"));
        await _db.SaveChangesAsync();

        var result = await _repo.GetAllAsync(repo: "/repo/b");

        result.Should().ContainSingle().Which.Id.Should().Be("s2");
    }

    [Fact]
    public async Task UpdateAsync_PersistsStatusAndEndedAt()
    {
        await _repo.AddAsync(NewSession("s1"));
        await _db.SaveChangesAsync();

        var session = await _repo.GetByIdAsync("s1");
        session!.Status = SessionStatus.Completed;
        session.EndedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(session);
        await _db.SaveChangesAsync();

        var fetched = await _repo.GetByIdAsync("s1");
        fetched!.Status.Should().Be(SessionStatus.Completed);
        fetched.EndedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AddTurnAsync_AddToolExecutionAsync_AddFileChangeAsync_PersistSingleEntities()
    {
        await _repo.AddAsync(NewSession("s1"));
        await _repo.AddTurnAsync(new ConversationTurn { Id = "t1", SessionId = "s1", TurnIndex = 0, Role = "user", Content = "hi", Timestamp = DateTime.UtcNow });
        await _repo.AddToolExecutionAsync(new ToolExecution { Id = "x1", SessionId = "s1", ToolName = "Read", Timestamp = DateTime.UtcNow });
        await _repo.AddFileChangeAsync(new FileChange { Id = "f1", SessionId = "s1", FilePath = "a.cs" });
        await _db.SaveChangesAsync();

        var fetched = await _repo.GetByIdAsync("s1");
        fetched!.ConversationTurns.Should().ContainSingle();
        fetched.ToolExecutions.Should().ContainSingle();
        fetched.FileChanges.Should().ContainSingle();
    }

    [Fact]
    public async Task GetAllAsync_FilterByDateRangeAndStatus_ReturnsMatchingOnly()
    {
        await _repo.AddAsync(NewSession("tooOld", startedAt: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
        var inRange = NewSession("inRange", startedAt: new DateTime(2026, 9, 23, 5, 0, 0, DateTimeKind.Utc));
        inRange.Status = SessionStatus.Completed;
        await _repo.AddAsync(inRange);
        await _repo.AddAsync(NewSession("wrongStatus", startedAt: new DateTime(2026, 9, 23, 6, 0, 0, DateTimeKind.Utc)));
        await _db.SaveChangesAsync();

        var result = await _repo.GetAllAsync(
            from: new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc),
            to: new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc),
            status: SessionStatus.Completed);

        result.Should().ContainSingle().Which.Id.Should().Be("inRange");
    }

    [Fact]
    public async Task GetAllAsync_DateRangeFilter_StillIncludesActiveSessionsStartedOutsideRange()
    {
        var resumed = NewSession("resumed-old", startedAt: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        resumed.Status = SessionStatus.Active;
        await _repo.AddAsync(resumed);
        var completedOld = NewSession("completed-old", startedAt: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        completedOld.Status = SessionStatus.Completed;
        await _repo.AddAsync(completedOld);
        await _db.SaveChangesAsync();

        var result = await _repo.GetAllAsync(
            from: new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc),
            to: new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc));

        result.Should().ContainSingle().Which.Id.Should().Be("resumed-old");
    }

    [Fact]
    public async Task GetAllAsync_ExplicitNonActiveStatusFilter_ExcludesOldActiveSessions()
    {
        var resumed = NewSession("resumed-old", startedAt: new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        resumed.Status = SessionStatus.Active;
        await _repo.AddAsync(resumed);
        await _db.SaveChangesAsync();

        var result = await _repo.GetAllAsync(
            from: new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc),
            to: new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc),
            status: SessionStatus.Completed);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDetailAsync_ReturnsCountsWithoutLoadingCollections()
    {
        await _repo.AddAsync(NewSession("s1"));
        await _repo.AddTurnsAsync(new[]
        {
            new ConversationTurn { Id = "t1", SessionId = "s1", TurnIndex = 0, Role = "user", Content = "hi", Timestamp = DateTime.UtcNow },
            new ConversationTurn { Id = "t2", SessionId = "s1", TurnIndex = 1, Role = "assistant", Content = "hi back", Timestamp = DateTime.UtcNow },
        });
        await _repo.AddToolExecutionsAsync(new[]
        {
            new ToolExecution { Id = "x1", SessionId = "s1", ToolName = "Read", Timestamp = DateTime.UtcNow },
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var detail = await _repo.GetDetailAsync("s1");

        detail.Should().NotBeNull();
        detail!.ConversationTurnCount.Should().Be(2);
        detail.ToolExecutionCount.Should().Be(1);
        detail.FileChangeCount.Should().Be(0);
    }

    [Fact]
    public async Task GetViolationCountsAsync_GroupsBySessionId()
    {
        await _repo.AddAsync(NewSession("s1"));
        await _repo.AddAsync(NewSession("s2"));
        _db.GovernanceViolations.AddRange(
            new GovernanceViolation { Id = "v1", SessionId = "s1", RuleId = "r", Message = "m" },
            new GovernanceViolation { Id = "v2", SessionId = "s1", RuleId = "r", Message = "m" },
            new GovernanceViolation { Id = "v3", SessionId = "s2", RuleId = "r", Message = "m" });
        await _db.SaveChangesAsync();

        var counts = await _repo.GetViolationCountsAsync(["s1", "s2"]);

        counts["s1"].Should().Be(2);
        counts["s2"].Should().Be(1);
    }

    [Fact]
    public async Task GetTimelinePageAsync_MergesTurnsAndToolsByTimestamp_NewestFirst()
    {
        var t0 = new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc);
        await _repo.AddAsync(NewSession("s1"));
        await _repo.AddTurnsAsync(new[]
        {
            new ConversationTurn { Id = "turn-1", SessionId = "s1", TurnIndex = 0, Role = "user", Content = "a", Timestamp = t0 },
            new ConversationTurn { Id = "turn-3", SessionId = "s1", TurnIndex = 2, Role = "assistant", Content = "c", Timestamp = t0.AddMinutes(2) },
        });
        await _repo.AddToolExecutionsAsync(new[]
        {
            new ToolExecution { Id = "tool-2", SessionId = "s1", ToolName = "Read", Timestamp = t0.AddMinutes(1) },
        });
        await _repo.AddFileChangesAsync(new[]
        {
            new FileChange { Id = "f1", SessionId = "s1", ToolExecutionId = "tool-2", FilePath = "a.cs" },
        });
        await _db.SaveChangesAsync();

        var page = await _repo.GetTimelinePageAsync("s1", before: null, limit: 50);

        // newest-first merge across both sources
        page.ConversationTurns.Select(t => t.Id).Should().Equal("turn-3", "turn-1");
        page.ToolExecutions.Select(t => t.Id).Should().ContainSingle("tool-2");
        // FileChanges scoped to tool executions actually present in this page
        page.FileChanges.Should().ContainSingle().Which.Id.Should().Be("f1");
        page.NextCursor.Should().Be(t0);
        page.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task GetTimelinePageAsync_BeforeCursor_ExcludesNewerEntries()
    {
        var t0 = new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc);
        await _repo.AddAsync(NewSession("s1"));
        await _repo.AddTurnsAsync(new[]
        {
            new ConversationTurn { Id = "old", SessionId = "s1", TurnIndex = 0, Role = "user", Content = "a", Timestamp = t0 },
            new ConversationTurn { Id = "new", SessionId = "s1", TurnIndex = 1, Role = "assistant", Content = "b", Timestamp = t0.AddMinutes(5) },
        });
        await _db.SaveChangesAsync();

        var page = await _repo.GetTimelinePageAsync("s1", before: t0.AddMinutes(5), limit: 50);

        page.ConversationTurns.Should().ContainSingle().Which.Id.Should().Be("old");
    }

    [Fact]
    public async Task GetTimelinePageAroundAsync_TurnAnchor_ReturnsContextOnBothSides()
    {
        var t0 = new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc);
        await _repo.AddAsync(NewSession("s1"));
        await _repo.AddTurnsAsync(new[]
        {
            new ConversationTurn { Id = "before", SessionId = "s1", TurnIndex = 0, Role = "user", Content = "a", Timestamp = t0 },
            new ConversationTurn { Id = "anchor", SessionId = "s1", TurnIndex = 1, Role = "assistant", Content = "b", Timestamp = t0.AddMinutes(1) },
            new ConversationTurn { Id = "after", SessionId = "s1", TurnIndex = 2, Role = "user", Content = "c", Timestamp = t0.AddMinutes(2) },
        });
        await _db.SaveChangesAsync();

        var page = await _repo.GetTimelinePageAroundAsync("s1", turnId: "anchor", toolExecutionId: null, limit: 50);

        page.Should().NotBeNull();
        page!.ConversationTurns.Select(t => t.Id).Should().Contain(["before", "anchor", "after"]);
    }

    [Fact]
    public async Task GetTimelinePageAroundAsync_UnknownAnchor_ReturnsNull()
    {
        await _repo.AddAsync(NewSession("s1"));
        await _db.SaveChangesAsync();

        var page = await _repo.GetTimelinePageAroundAsync("s1", turnId: "missing", toolExecutionId: null, limit: 50);

        page.Should().BeNull();
    }

    [Fact]
    public async Task GetTimelinePageAroundAsync_ToolAnchor_ResolvesByToolExecutionId()
    {
        var t0 = new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc);
        await _repo.AddAsync(NewSession("s1"));
        await _repo.AddToolExecutionsAsync(new[]
        {
            new ToolExecution { Id = "tool-anchor", SessionId = "s1", ToolName = "Edit", Timestamp = t0 },
        });
        await _db.SaveChangesAsync();

        var page = await _repo.GetTimelinePageAroundAsync("s1", turnId: null, toolExecutionId: "tool-anchor", limit: 50);

        page.Should().NotBeNull();
        page!.ToolExecutions.Should().ContainSingle().Which.Id.Should().Be("tool-anchor");
    }

    [Fact]
    public async Task GetTurnUsageRowsForDateRangeAsync_FilterByDeveloper_ReturnsOnlyThatDevelopersTurns()
    {
        await _repo.AddAsync(NewSession("s1", email: "alice@test.com"));
        await _repo.AddAsync(NewSession("s2", email: "bob@test.com"));
        await _repo.AddTurnsAsync(new[]
        {
            new ConversationTurn { Id = "t1", SessionId = "s1", TurnIndex = 0, Role = "user", Content = "a", Timestamp = DateTime.UtcNow, EstimatedCostUsd = 1.00m },
            new ConversationTurn { Id = "t2", SessionId = "s2", TurnIndex = 0, Role = "user", Content = "b", Timestamp = DateTime.UtcNow, EstimatedCostUsd = 2.00m },
        });
        await _db.SaveChangesAsync();

        var result = await _repo.GetTurnUsageRowsForDateRangeAsync(
            from: null, to: null, developerEmail: "alice@test.com");

        result.Should().ContainSingle().Which.EstimatedCostUsd.Should().Be(1.00m);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
