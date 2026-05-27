namespace DeveloperOverwatch.Application.Queries.GetDeveloperAnalytics;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetDeveloperAnalyticsQueryHandler
    : IRequestHandler<GetDeveloperAnalyticsQuery, Result<DeveloperAnalytics>>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IGovernanceViolationRepository _violationRepository;
    private readonly ICommitSessionLinkRepository _commitLinkRepository;

    public GetDeveloperAnalyticsQueryHandler(
        ISessionRepository sessionRepository,
        IGovernanceViolationRepository violationRepository,
        ICommitSessionLinkRepository commitLinkRepository)
    {
        _sessionRepository = sessionRepository;
        _violationRepository = violationRepository;
        _commitLinkRepository = commitLinkRepository;
    }

    public async Task<Result<DeveloperAnalytics>> Handle(GetDeveloperAnalyticsQuery request, CancellationToken cancellationToken)
    {
        var sessions = await _sessionRepository.GetAllAsync(developerEmail: request.Email, ct: cancellationToken);
        if (sessions.Count == 0)
            return Result<DeveloperAnalytics>.Fail($"Developer '{request.Email}' not found");

        var sessionIds = sessions.Select(s => s.Id).ToHashSet();

        var allViolations = await _violationRepository.GetAllAsync(cancellationToken);
        var violations = allViolations.Where(v => sessionIds.Contains(v.SessionId)).ToList();

        var allCommitLinks = await _commitLinkRepository.GetAllAsync(cancellationToken);
        var commitLinks = allCommitLinks.Where(c => sessionIds.Contains(c.SessionId)).ToList();

        var oneWeekAgo = DateTime.UtcNow.AddDays(-7);

        var analytics = new DeveloperAnalytics(
            Email: request.Email,
            TotalSessions: sessions.Count,
            SessionsThisWeek: sessions.Count(s => s.StartedAt >= oneWeekAgo),
            TotalViolations: violations.Count,
            CriticalViolations: violations.Count(v => v.Severity == ViolationSeverity.Critical),
            ErrorViolations: violations.Count(v => v.Severity == ViolationSeverity.Error),
            WarningViolations: violations.Count(v => v.Severity == ViolationSeverity.Warning),
            InfoViolations: violations.Count(v => v.Severity == ViolationSeverity.Info),
            TotalCommits: commitLinks.Select(c => c.CommitSha).Distinct().Count(),
            RecentRepos: sessions
                .Select(s => s.RepoPath)
                .Where(r => !string.IsNullOrEmpty(r))
                .Select(r => r!)
                .Distinct()
                .ToList()
                .AsReadOnly());

        return Result<DeveloperAnalytics>.Success(analytics);
    }
}
