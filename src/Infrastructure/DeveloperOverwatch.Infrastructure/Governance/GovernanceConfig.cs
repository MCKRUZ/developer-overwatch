namespace DeveloperOverwatch.Infrastructure.Governance;

public sealed class GovernanceConfig
{
    public string PoliciesPath { get; set; } = "policies";
    public int DriftWindowHours { get; set; } = 72;
    public int DriftThreshold { get; set; } = 3;
}
