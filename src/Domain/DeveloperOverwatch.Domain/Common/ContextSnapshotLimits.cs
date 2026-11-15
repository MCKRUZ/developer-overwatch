namespace DeveloperOverwatch.Domain.Common;

/// <summary>Bounds on a stored context snapshot, shared by the database columns and the request validator.</summary>
public static class ContextSnapshotLimits
{
    public const int ClaudeCodeVersionLength = 50;
    public const int NameLength = 255;
    public const int TriggerLength = 20;

    public const int MaxCategories = 20;
    public const int MaxTools = 200;
    public const int MaxCompactions = 50;
}
