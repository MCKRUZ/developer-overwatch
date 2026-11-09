namespace DeveloperOverwatch.Application.Services;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;

public sealed class SessionCommitCorrelator
{
    private static readonly TimeSpan DefaultTimeWindow = TimeSpan.FromHours(2);

    private readonly IGitHostingService _gitService;
    private readonly ISessionRepository _sessionRepo;

    public SessionCommitCorrelator(IGitHostingService gitService, ISessionRepository sessionRepo)
    {
        _gitService = gitService;
        _sessionRepo = sessionRepo;
    }

    public async Task<IReadOnlyList<CommitSessionLink>> CorrelateCommitAsync(
        string repo,
        string commitSha,
        DateTime commitTime,
        TimeSpan? timeWindow = null,
        CancellationToken ct = default)
    {
        var window = timeWindow ?? DefaultTimeWindow;
        var commitFiles = await _gitService.GetCommitFilesAsync(repo, commitSha, ct);

        if (commitFiles.Count == 0)
            return [];

        var commitFileSet = new HashSet<string>(commitFiles, StringComparer.OrdinalIgnoreCase);
        var sessions = await _sessionRepo.GetAllAsync(repo: repo, ct: ct);

        var windowStart = commitTime - window;
        var candidates = sessions
            .Where(s => s.Status == SessionStatus.Completed
                        && s.EndedAt.HasValue
                        && s.EndedAt.Value >= windowStart
                        && s.EndedAt.Value <= commitTime);

        var links = new List<CommitSessionLink>();
        foreach (var session in candidates)
        {
            var sessionFilePaths = session.FileChanges
                .Select(fc => fc.FilePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var matchedCount = commitFileSet.Count(f => sessionFilePaths.Contains(f));
            if (matchedCount == 0)
                continue;

            var confidence = (double)matchedCount / commitFiles.Count;

            links.Add(new CommitSessionLink
            {
                Id = Guid.NewGuid().ToString(),
                CommitSha = commitSha,
                SessionId = session.Id,
                Repo = repo,
                Confidence = Math.Round(confidence, 4),
                LinkMethod = "file_match"
            });
        }

        return links;
    }

    public static IReadOnlyList<CommitSessionLink> ParseTrailers(
        string repo, string commitSha, string commitMessage)
    {
        var links = new List<CommitSessionLink>();
        var lines = commitMessage.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("Claude-Session:", StringComparison.OrdinalIgnoreCase))
                continue;

            var sessionId = trimmed["Claude-Session:".Length..].Trim();
            if (string.IsNullOrWhiteSpace(sessionId))
                continue;

            links.Add(new CommitSessionLink
            {
                Id = Guid.NewGuid().ToString(),
                CommitSha = commitSha,
                SessionId = sessionId,
                Repo = repo,
                Confidence = 1.0,
                LinkMethod = "trailer"
            });
        }

        return links;
    }
}
