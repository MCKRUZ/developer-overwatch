namespace DeveloperOverwatch.Agent.Models;

/// <summary>What one usage write did: responses newly stored, and responses that were already there.</summary>
public readonly record struct RecordedUsage(int Added, int AlreadyPresent);
