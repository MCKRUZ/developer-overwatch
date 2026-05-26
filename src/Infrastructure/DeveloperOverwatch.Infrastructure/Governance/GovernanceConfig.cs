namespace DeveloperOverwatch.Infrastructure.Governance;

public sealed class GovernanceConfig
{
    public string PoliciesPath { get; init; } = "policies";
    public int DriftWindowHours { get; init; } = 72;
    public int DriftThreshold { get; init; } = 3;
}
