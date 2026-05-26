namespace DeveloperOverwatch.Infrastructure.Persistence.Repositories;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

public sealed class PullRequestReviewRepository : IPullRequestReviewRepository
{
    private readonly OverwatchDbContext _db;

    public PullRequestReviewRepository(OverwatchDbContext db) => _db = db;

    public async Task<PullRequestReview?> GetByRepoAndNumberAsync(
        string repo, int prNumber, CancellationToken ct = default) =>
        await _db.PullRequestReviews
            .FirstOrDefaultAsync(r => r.Repo == repo && r.PrNumber == prNumber, ct);

    public async Task<IReadOnlyList<PullRequestReview>> GetAllAsync(
        string? repo = null, CancellationToken ct = default)
    {
        var query = _db.PullRequestReviews.AsQueryable();
        if (repo is not null)
            query = query.Where(r => r.Repo == repo);
        return await query.OrderByDescending(r => r.PrNumber).ToListAsync(ct);
    }

    public async Task AddAsync(PullRequestReview review, CancellationToken ct = default) =>
        await _db.PullRequestReviews.AddAsync(review, ct);

    public Task UpdateAsync(PullRequestReview review, CancellationToken ct = default)
    {
        _db.PullRequestReviews.Update(review);
        return Task.CompletedTask;
    }
}
