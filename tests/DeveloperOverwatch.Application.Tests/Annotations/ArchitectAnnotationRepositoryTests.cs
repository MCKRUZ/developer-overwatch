namespace DeveloperOverwatch.Application.Tests.Annotations;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Infrastructure.Persistence;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class ArchitectAnnotationRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OverwatchDbContext _db;
    private readonly ArchitectAnnotationRepository _repo;

    public ArchitectAnnotationRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OverwatchDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new OverwatchDbContext(options);
        _db.Database.EnsureCreated();
        _repo = new ArchitectAnnotationRepository(_db);
    }

    [Fact]
    public async Task AddAndGetById_ReturnsAnnotation()
    {
        var session = new Session { Id = "s1", DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow };
        await _db.Sessions.AddAsync(session);
        await _db.SaveChangesAsync();

        var annotation = new ArchitectAnnotation
        {
            Id = "a1",
            SessionId = "s1",
            Content = "Good approach here",
            CreatedBy = "architect@test.com",
            CreatedAt = DateTime.UtcNow
        };
        await _repo.AddAsync(annotation);
        await _db.SaveChangesAsync();

        var result = await _repo.GetByIdAsync("a1");

        result.Should().NotBeNull();
        result!.Content.Should().Be("Good approach here");
        result.CreatedBy.Should().Be("architect@test.com");
    }

    [Fact]
    public async Task GetBySessionId_ReturnsAnnotationsForSession()
    {
        var session = new Session { Id = "s2", DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow };
        await _db.Sessions.AddAsync(session);
        await _db.SaveChangesAsync();

        await _repo.AddAsync(new ArchitectAnnotation { Id = "a2", SessionId = "s2", Content = "Note 1", CreatedBy = "arch@test.com", CreatedAt = DateTime.UtcNow });
        await _repo.AddAsync(new ArchitectAnnotation { Id = "a3", SessionId = "s2", Content = "Note 2", CreatedBy = "arch@test.com", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _repo.GetBySessionIdAsync("s2");

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetBySessionId_NoAnnotations_ReturnsEmpty()
    {
        var result = await _repo.GetBySessionIdAsync("nonexistent");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetById_NotFound_ReturnsNull()
    {
        var result = await _repo.GetByIdAsync("nonexistent");

        result.Should().BeNull();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
