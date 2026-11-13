namespace DeveloperOverwatch.Domain.Enums;

/// <summary>What became of a save that may collide with a unique rule, such as one budget per subject and period.</summary>
public enum SaveOutcome
{
    /// <summary>Every change was written.</summary>
    Saved,

    /// <summary>A unique index refused the save, so nothing was written: another request stored the same key first.</summary>
    Duplicate,
}
