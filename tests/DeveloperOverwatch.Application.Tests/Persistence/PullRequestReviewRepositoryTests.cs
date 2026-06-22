namespace DeveloperOverwatch.Application.Tests.Persistence;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Infrastructure.Persistence;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class PullRequestReviewRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OverwatchDbContext _db;
    private readonly PullRequestReviewRepository _repo;

    public PullRequestReviewRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<OverwatchDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new OverwatchDbContext(options);
        _db.Database.EnsureCreated();
        _repo = new PullRequestReviewRepository(_db);
    }

    private static PullRequestReview NewReview(string id, string repo, int prNumber) => new()
    {
        Id = id,
        Repo = repo,
        PrNumber = prNumber,
        Status = ReviewStatus.Pending,
    };

    [Fact]
    public async Task AddAsync_ThenGetByRepoAndNumber_ReturnsReview()
    {
        await _repo.AddAsync(NewReview("r1", "owner/repo", 7));
        await _db.SaveChangesAsync();

        var fetched = await _repo.GetByRepoAndNumberAsync("owner/repo", 7);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be("r1");
    }

    [Fact]
    public async Task GetByRepoAndNumberAsync_WrongRepo_ReturnsNull()
    {
        await _repo.AddAsync(NewReview("r1", "owner/repo", 7));
        await _db.SaveChangesAsync();

        var fetched = await _repo.GetByRepoAndNumberAsync("other/repo", 7);

        fetched.Should().BeNull();
    }

    [Fact]
    public async Task GetByRepoAndNumberAsync_WrongNumber_ReturnsNull()
    {
        await _repo.AddAsync(NewReview("r1", "owner/repo", 7));
        await _db.SaveChangesAsync();

        var fetched = await _repo.GetByRepoAndNumberAsync("owner/repo", 99);

        fetched.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_NoFilter_ReturnsAllOrderedByPrNumberDescending()
    {
        await _repo.AddAsync(NewReview("r1", "owner/repo", 1));
        await _repo.AddAsync(NewReview("r2", "owner/repo", 10));
        await _repo.AddAsync(NewReview("r3", "owner/repo", 5));
        await _db.SaveChangesAsync();

        var all = await _repo.GetAllAsync();

        all.Select(r => r.PrNumber).Should().Equal(10, 5, 1);
    }

    [Fact]
    public async Task GetAllAsync_FilterByRepo_ReturnsMatchingOnly()
    {
        await _repo.AddAsync(NewReview("r1", "owner/a", 1));
        await _repo.AddAsync(NewReview("r2", "owner/b", 2));
        await _db.SaveChangesAsync();

        var result = await _repo.GetAllAsync(repo: "owner/b");

        result.Should().ContainSingle().Which.Id.Should().Be("r2");
    }

    [Fact]
    public async Task UpdateAsync_PersistsReviewDecision()
    {
        await _repo.AddAsync(NewReview("r1", "owner/repo", 7));
        await _db.SaveChangesAsync();

        var review = await _repo.GetByRepoAndNumberAsync("owner/repo", 7);
        review!.Status = ReviewStatus.Approved;
        review.ReviewedBy = "architect@test.com";
        review.ReviewedAt = DateTime.UtcNow;
        review.Notes = "LGTM";
        await _repo.UpdateAsync(review);
        await _db.SaveChangesAsync();

        var fetched = await _repo.GetByRepoAndNumberAsync("owner/repo", 7);
        fetched!.Status.Should().Be(ReviewStatus.Approved);
        fetched.ReviewedBy.Should().Be("architect@test.com");
        fetched.Notes.Should().Be("LGTM");
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
