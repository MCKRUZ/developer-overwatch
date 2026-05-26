namespace DeveloperOverwatch.Domain.Interfaces;

public interface IGitHostingService
{
    Task<IReadOnlyList<string>> GetCommitFilesAsync(string repo, string commitSha, CancellationToken ct = default);
    Task<IReadOnlyList<CommitInfo>> GetPullRequestCommitsAsync(string repo, int prNumber, CancellationToken ct = default);
    Task<PullRequestInfo?> GetPullRequestAsync(string repo, int prNumber, CancellationToken ct = default);
}

public sealed record CommitInfo(string Sha, string Message, DateTime CommittedAt);

public sealed record PullRequestInfo(
    int Number,
    string Title,
    string AuthorEmail,
    string HeadBranch,
    string BaseBranch,
    string State);
