namespace DeveloperOverwatch.Domain.Interfaces;

using DeveloperOverwatch.Domain.Entities;

public interface ICommitSessionLinkRepository
{
    Task<IReadOnlyList<CommitSessionLink>> GetByCommitShaAsync(string commitSha, CancellationToken ct = default);
    Task<IReadOnlyList<CommitSessionLink>> GetBySessionIdAsync(string sessionId, CancellationToken ct = default);
    Task AddAsync(CommitSessionLink link, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<CommitSessionLink> links, CancellationToken ct = default);
}
