namespace DeveloperOverwatch.Application.Models;

public sealed record DeveloperAnalytics(
    string Email,
    int TotalSessions,
    int SessionsThisWeek,
    int TotalViolations,
    int CriticalViolations,
    int ErrorViolations,
    int WarningViolations,
    int InfoViolations,
    int TotalCommits,
    IReadOnlyList<string> RecentRepos);
