namespace DeveloperOverwatch.Application.Tests.Persistence;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Infrastructure.Persistence;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>The existence lookups ingest dedupes against. Item ids are primary keys or come from the
/// transcript itself, so each lookup spans every session.</summary>
public sealed class SessionRepositoryIdLookupTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OverwatchDbContext _db;
    private readonly SessionRepository _repo;

    public SessionRepositoryIdLookupTests()
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

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static Session NewSession(string id) =>
        new() { Id = id, DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow };

    [Fact]
    public async Task GetExistingTurnIdsAsync_ReturnsMatchingIdsFromEverySession()
    {
        await _repo.AddAsync(NewSession("s1"));
        await _repo.AddAsync(NewSession("s2"));
        await _repo.AddTurnsAsync(
        [
            new ConversationTurn { Id = "t-in-s1", SessionId = "s1", Role = "assistant", Timestamp = DateTime.UtcNow },
            new ConversationTurn { Id = "t-in-s2", SessionId = "s2", Role = "assistant", Timestamp = DateTime.UtcNow },
        ]);
        await _db.SaveChangesAsync();

        var existing = await _repo.GetExistingTurnIdsAsync(["t-in-s1", "t-in-s2", "t-new"]);

        existing.Should().BeEquivalentTo(["t-in-s1", "t-in-s2"]);
    }

    [Fact]
    public async Task GetExistingToolExecutionIdsAsync_ReturnsMatchingIdsFromEverySession()
    {
        await _repo.AddAsync(NewSession("s1"));
        await _repo.AddAsync(NewSession("s2"));
        await _repo.AddToolExecutionsAsync(
        [
            new ToolExecution { Id = "x-in-s1", SessionId = "s1", ToolName = "Read", Timestamp = DateTime.UtcNow },
            new ToolExecution { Id = "x-in-s2", SessionId = "s2", ToolName = "Edit", Timestamp = DateTime.UtcNow },
        ]);
        await _db.SaveChangesAsync();

        var existing = await _repo.GetExistingToolExecutionIdsAsync(["x-in-s1", "x-in-s2", "x-new"]);

        existing.Should().BeEquivalentTo(["x-in-s1", "x-in-s2"]);
    }

    [Fact]
    public async Task GetExistingFileChangeIdsAsync_ReturnsMatchingIdsFromEverySession()
    {
        await _repo.AddAsync(NewSession("s1"));
        await _repo.AddAsync(NewSession("s2"));
        await _repo.AddFileChangesAsync(
        [
            new FileChange { Id = "c-in-s1", SessionId = "s1", FilePath = "a.cs" },
            new FileChange { Id = "c-in-s2", SessionId = "s2", FilePath = "b.cs" },
        ]);
        await _db.SaveChangesAsync();

        var existing = await _repo.GetExistingFileChangeIdsAsync(["c-in-s1", "c-in-s2", "c-new"]);

        existing.Should().BeEquivalentTo(["c-in-s1", "c-in-s2"]);
    }

    [Fact]
    public async Task GetExistingToolFailureIdsAsync_StoredFailures_ReturnsOnlyStoredIds()
    {
        await _repo.AddAsync(NewSession("s1"));
        await _repo.AddToolFailuresAsync(
            [new SessionToolFailure { Id = "f-1", SessionId = "s1", ToolName = "Bash", At = DateTime.UtcNow }]);
        await _db.SaveChangesAsync();

        var existing = await _repo.GetExistingToolFailureIdsAsync(["f-1", "f-new"]);

        existing.Should().BeEquivalentTo(["f-1"]);
    }
}
