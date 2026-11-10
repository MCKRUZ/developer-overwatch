namespace DeveloperOverwatch.Domain.Enums;

/// <summary>What the coding agent is doing right now, as last reported by the Agent.</summary>
public enum ActivityState
{
    /// <summary>Working on a request.</summary>
    Active,

    /// <summary>Blocked on the developer (a permission prompt or a question).</summary>
    Waiting,

    /// <summary>Finished its turn and has nothing in progress.</summary>
    Idle
}
