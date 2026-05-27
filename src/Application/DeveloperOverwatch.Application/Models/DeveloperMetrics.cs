namespace DeveloperOverwatch.Application.Models;

public sealed record DeveloperMetrics(
    string Email,
    int SessionCount,
    int ViolationCount,
    int CommitCount);
