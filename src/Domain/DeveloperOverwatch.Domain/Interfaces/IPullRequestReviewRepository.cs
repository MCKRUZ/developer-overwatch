namespace DeveloperOverwatch.Domain.Interfaces;

using DeveloperOverwatch.Domain.Entities;

public interface IPullRequestReviewRepository
{
    Task<PullRequestReview?> GetByRepoAndNumberAsync(string repo, int prNumber, CancellationToken ct = default);
    Task<IReadOnlyList<PullRequestReview>> GetAllAsync(string? repo = null, CancellationToken ct = default);
    Task AddAsync(PullRequestReview review, CancellationToken ct = default);
    Task UpdateAsync(PullRequestReview review, CancellationToken ct = default);
}
