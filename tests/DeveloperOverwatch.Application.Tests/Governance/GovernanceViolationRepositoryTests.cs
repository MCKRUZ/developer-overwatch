namespace DeveloperOverwatch.Application.Tests.Governance;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Infrastructure.Persistence;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class GovernanceViolationRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OverwatchDbContext _db;
    private readonly GovernanceViolationRepository _repo;

    public GovernanceViolationRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OverwatchDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new OverwatchDbContext(options);
        _db.Database.EnsureCreated();
        _repo = new GovernanceViolationRepository(_db);
    }

    [Fact]
    public async Task AddRangeAndGetBySessionId_ReturnsViolations()
    {
        var session = new Session { Id = "s1", DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow };
        await _db.Sessions.AddAsync(session);
        await _db.SaveChangesAsync();

        var violations = new[]
        {
            new GovernanceViolation { Id = "v1", SessionId = "s1", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "test 1", CreatedAt = DateTime.UtcNow },
            new GovernanceViolation { Id = "v2", SessionId = "s1", RuleId = "r2", Severity = ViolationSeverity.Warning, Message = "test 2", CreatedAt = DateTime.UtcNow }
        };
        await _repo.AddRangeAsync(violations);
        await _db.SaveChangesAsync();

        var result = await _repo.GetBySessionIdAsync("s1");

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetBySessionId_WithSeverityFilter_FiltersCorrectly()
    {
        var session = new Session { Id = "s2", DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow };
        await _db.Sessions.AddAsync(session);
        var violations = new[]
        {
            new GovernanceViolation { Id = "v3", SessionId = "s2", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "err", CreatedAt = DateTime.UtcNow },
            new GovernanceViolation { Id = "v4", SessionId = "s2", RuleId = "r2", Severity = ViolationSeverity.Info, Message = "info", CreatedAt = DateTime.UtcNow }
        };
        await _repo.AddRangeAsync(violations);
        await _db.SaveChangesAsync();

        var result = await _repo.GetBySessionIdAsync("s2", ViolationSeverity.Error);

        result.Should().ContainSingle().Which.Id.Should().Be("v3");
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsWithinWindow()
    {
        var session = new Session { Id = "s3", DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow };
        await _db.Sessions.AddAsync(session);
        var violations = new[]
        {
            new GovernanceViolation { Id = "v5", SessionId = "s3", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "recent", CreatedAt = DateTime.UtcNow.AddHours(-1) },
            new GovernanceViolation { Id = "v6", SessionId = "s3", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "old", CreatedAt = DateTime.UtcNow.AddDays(-10) }
        };
        await _repo.AddRangeAsync(violations);
        await _db.SaveChangesAsync();

        var result = await _repo.GetRecentAsync(TimeSpan.FromHours(24));

        result.Should().ContainSingle().Which.Id.Should().Be("v5");
    }

    [Fact]
    public async Task UpdateAsync_PersistsChanges()
    {
        var session = new Session { Id = "s4", DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow };
        await _db.Sessions.AddAsync(session);
        var violation = new GovernanceViolation { Id = "v7", SessionId = "s4", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "test", CreatedAt = DateTime.UtcNow };
        await _repo.AddRangeAsync([violation]);
        await _db.SaveChangesAsync();

        violation.Resolved = true;
        violation.ResolvedBy = "architect@test.com";
        violation.ResolutionReason = "false positive";
        violation.ResolvedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(violation);
        await _db.SaveChangesAsync();

        var fetched = await _repo.GetByIdAsync("v7");
        fetched.Should().NotBeNull();
        fetched!.Resolved.Should().BeTrue();
        fetched.ResolutionReason.Should().Be("false positive");
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
