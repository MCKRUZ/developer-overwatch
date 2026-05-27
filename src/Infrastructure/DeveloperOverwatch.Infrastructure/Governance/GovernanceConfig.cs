namespace DeveloperOverwatch.Infrastructure.Governance;

public sealed class GovernanceConfig
{
    public string PoliciesPath { get; init; } = "policies";
    public int DriftWindowHours { get; init; } = 72;
    public int DriftThreshold { get; init; } = 3;
    public double DriftAlpha { get; init; } = 0.3;
    public int DriftBucketHours { get; init; } = 4;
    public string? LlmApiUrl { get; init; }
    public string? LlmApiKey { get; init; }
    public string LlmModel { get; init; } = "claude-sonnet-4-6";
    public int LlmMaxTokens { get; init; } = 1024;
    public int LlmTimeoutSeconds { get; init; } = 30;
}
