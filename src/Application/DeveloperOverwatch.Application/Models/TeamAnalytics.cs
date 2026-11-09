namespace DeveloperOverwatch.Application.Models;

public sealed record TeamAnalytics(
    int TotalSessions,
    int SessionsThisWeek,
    int TotalPullRequests,
    int ApprovedPullRequests,
    int PendingPullRequests,
    int NeedsChangesPullRequests,
    int TotalViolations,
    int ViolationsThisWeek,
    IReadOnlyList<DeveloperMetrics> Developers,
    IReadOnlyList<DailyCostUsage> DailyCostTrend,
    decimal DailyCostThresholdUsd);

public sealed record DailyCostUsage(
    DateOnly Date,
    long InputTokens,
    long OutputTokens,
    long CacheCreationInputTokens,
    long CacheReadInputTokens,
    decimal EstimatedCostUsd);
