namespace DeveloperOverwatch.Application.Models;

using System.Text.Json.Serialization;

/// <param name="DailyCostThresholdUsd">The team daily budget, or null when there is none. Always written,
/// as an explicit null, despite the API's global ignore-nulls setting.</param>
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] decimal? DailyCostThresholdUsd);

public sealed record DailyCostUsage(
    DateOnly Date,
    long InputTokens,
    long OutputTokens,
    long CacheCreationInputTokens,
    long CacheReadInputTokens,
    decimal EstimatedCostUsd);
