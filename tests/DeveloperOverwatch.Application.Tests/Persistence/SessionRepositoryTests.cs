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

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
