namespace DeveloperOverwatch.Application.Tests.Persistence;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Infrastructure.Persistence;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class CommitSessionLinkRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OverwatchDbContext _db;
    private readonly CommitSessionLinkRepository _repo;

    public CommitSessionLinkRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OverwatchDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new OverwatchDbContext(options);
        _db.Database.EnsureCreated();
        _repo = new CommitSessionLinkRepository(_db);
    }

    private async Task SeedSessionAsync(string id, string email = "dev@test.com")
    {
        await _db.Sessions.AddAsync(new Session { Id = id, DeveloperEmail = email, StartedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
    }

    private static CommitSessionLink NewLink(string id, string sha, string sessionId, string repo = "owner/repo") => new()
    {
        Id = id,
        CommitSha = sha,
        SessionId = sessionId,
        Repo = repo,
        Confidence = 0.9,
        LinkMethod = "trailer",
    };

    [Fact]
    public async Task AddAsync_ThenGetByCommitSha_ReturnsLinkWithSessionIncluded()
    {
        await SeedSessionAsync("s1", "alice@test.com");
        await _repo.AddAsync(NewLink("l1", "sha-abc", "s1"));
        await _db.SaveChangesAsync();

        var result = await _repo.GetByCommitShaAsync("sha-abc");

        var link = result.Should().ContainSingle().Subject;
        link.Id.Should().Be("l1");
        link.Session.Should().NotBeNull();
        link.Session!.DeveloperEmail.Should().Be("alice@test.com");
    }

    [Fact]
    public async Task GetByCommitShaAsync_NoMatch_ReturnsEmpty()
    {
        var result = await _repo.GetByCommitShaAsync("nope");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBySessionIdAsync_ReturnsAllLinksForSession()
    {
        await SeedSessionAsync("s1");
        await _repo.AddRangeAsync(new[]
        {
            NewLink("l1", "sha-1", "s1"),
            NewLink("l2", "sha-2", "s1"),
        });
        await _db.SaveChangesAsync();

        var result = await _repo.GetBySessionIdAsync("s1");

        result.Should().HaveCount(2);
        result.Select(l => l.Id).Should().BeEquivalentTo("l1", "l2");
    }

    [Fact]
    public async Task GetBySessionIdAsync_OtherSessionLinksExcluded()
    {
        await SeedSessionAsync("s1");
        await SeedSessionAsync("s2");
        await _repo.AddRangeAsync(new[]
        {
            NewLink("l1", "sha-1", "s1"),
            NewLink("l2", "sha-2", "s2"),
        });
        await _db.SaveChangesAsync();

        var result = await _repo.GetBySessionIdAsync("s1");

        result.Should().ContainSingle().Which.Id.Should().Be("l1");
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEveryLink()
    {
        await SeedSessionAsync("s1");
        await _repo.AddRangeAsync(new[]
        {
            NewLink("l1", "sha-1", "s1"),
            NewLink("l2", "sha-2", "s1"),
        });
        await _db.SaveChangesAsync();

        var result = await _repo.GetAllAsync();

        result.Should().HaveCount(2);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
