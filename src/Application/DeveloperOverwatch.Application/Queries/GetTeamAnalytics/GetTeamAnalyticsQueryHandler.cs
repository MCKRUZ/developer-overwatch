namespace DeveloperOverwatch.Application.Queries.GetTeamAnalytics;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetTeamAnalyticsQueryHandler
    : IRequestHandler<GetTeamAnalyticsQuery, Result<TeamAnalytics>>
{
    private readonly ISessionRepository _sessionRepository;
    private readonly IPullRequestReviewRepository _prRepository;
    private readonly IGovernanceViolationRepository _violationRepository;
    private readonly ICommitSessionLinkRepository _commitLinkRepository;
    private readonly IBudgetRepository _budgetRepository;

    public GetTeamAnalyticsQueryHandler(
        ISessionRepository sessionRepository,
        IPullRequestReviewRepository prRepository,
        IGovernanceViolationRepository violationRepository,
        ICommitSessionLinkRepository commitLinkRepository,
        IBudgetRepository budgetRepository)
    {
        _sessionRepository = sessionRepository;
        _prRepository = prRepository;
        _violationRepository = violationRepository;
        _commitLinkRepository = commitLinkRepository;
        _budgetRepository = budgetRepository;
    }

    public async Task<Result<TeamAnalytics>> Handle(GetTeamAnalyticsQuery request, CancellationToken cancellationToken)
    {
        var sessions = await _sessionRepository.GetAllAsync(
            developerEmail: request.Developer, ct: cancellationToken);
        var prs = await _prRepository.GetAllAsync(ct: cancellationToken);
        var violations = await _violationRepository.GetAllAsync(cancellationToken);
        var commitLinks = await _commitLinkRepository.GetAllAsync(cancellationToken);
        var usageRows = await _sessionRepository.GetTurnUsageRowsForDateRangeAsync(
            request.From, request.To, request.Developer, cancellationToken);
        var teamDailyBudget = await _budgetRepository.FindAsync(
            Budget.TeamSubjectKey, BudgetPeriod.Daily, cancellationToken);

        var oneWeekAgo = DateTime.UtcNow.AddDays(-7);

        var sessionsByDeveloper = sessions
            .GroupBy(s => s.DeveloperEmail)
            .ToDictionary(g => g.Key, g => g.Select(s => s.Id).ToHashSet());

        var developers = sessionsByDeveloper
            .Select(kvp => new DeveloperMetrics(
                kvp.Key,
                kvp.Value.Count,
                violations.Count(v => kvp.Value.Contains(v.SessionId)),
                commitLinks.Count(c => kvp.Value.Contains(c.SessionId))))
            .OrderByDescending(d => d.SessionCount)
            .ToList()
            .AsReadOnly();

        var dailyCostTrend = usageRows
            .GroupBy(r => DateOnly.FromDateTime(r.Timestamp))
            .OrderBy(g => g.Key)
            .Select(g => new DailyCostUsage(
                g.Key,
                g.Sum(r => (long)(r.InputTokens ?? 0)),
                g.Sum(r => (long)(r.OutputTokens ?? 0)),
                g.Sum(r => (long)(r.CacheCreationInputTokens ?? 0)),
                g.Sum(r => (long)(r.CacheReadInputTokens ?? 0)),
                g.Sum(r => r.EstimatedCostUsd ?? 0m)))
            .ToList()
            .AsReadOnly();

        var analytics = new TeamAnalytics(
            TotalSessions: sessions.Count,
            SessionsThisWeek: sessions.Count(s => s.StartedAt >= oneWeekAgo),
            TotalPullRequests: prs.Count,
            ApprovedPullRequests: prs.Count(p => p.Status == ReviewStatus.Approved),
            PendingPullRequests: prs.Count(p => p.Status == ReviewStatus.Pending),
            NeedsChangesPullRequests: prs.Count(p => p.Status == ReviewStatus.NeedsChanges),
            TotalViolations: violations.Count,
            ViolationsThisWeek: violations.Count(v => v.CreatedAt >= oneWeekAgo),
            Developers: developers,
            DailyCostTrend: dailyCostTrend,
            DailyCostThresholdUsd: teamDailyBudget?.AmountUsd);

        return Result<TeamAnalytics>.Success(analytics);
    }
}
