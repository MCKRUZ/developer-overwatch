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
    IReadOnlyList<DeveloperMetrics> Developers);
