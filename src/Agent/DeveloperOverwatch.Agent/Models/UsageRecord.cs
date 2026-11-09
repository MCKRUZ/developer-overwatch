namespace DeveloperOverwatch.Agent.Models;

/// <summary>The usage of one API response, as kept on this machine. Counts and labels only.</summary>
public sealed record UsageRecord(
    string SourceId,
    string SessionId,
    DateTime Timestamp,
    string? Model,
    int InputTokens,
    int OutputTokens,
    int CacheReadTokens,
    int CacheCreationTokens,
    string? ProjectKey,
    string? GitBranch,
    string? SubagentName);
