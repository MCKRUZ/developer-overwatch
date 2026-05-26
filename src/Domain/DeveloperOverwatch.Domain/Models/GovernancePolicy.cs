namespace DeveloperOverwatch.Domain.Models;

using DeveloperOverwatch.Domain.Enums;

public sealed record GovernancePolicy
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ViolationSeverity Severity { get; init; }
    public required PolicyType Type { get; init; }
    public required IReadOnlyList<ScanTarget> Scan { get; init; }
    public IReadOnlyList<string> Patterns { get; init; } = [];
    public IReadOnlyList<string> ExcludePaths { get; init; } = [];
    public string? Rule { get; init; }
}
