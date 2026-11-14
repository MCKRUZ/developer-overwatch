namespace DeveloperOverwatch.Domain.Metering;

/// <summary>Maps a used fraction of a limit to its <see cref="BudgetLevel"/>. Both bands are inclusive.</summary>
public static class BudgetThreshold
{
    public const decimal AmberAt = 0.75m;
    public const decimal RedAt = 1m;

    public static BudgetLevel LevelOf(decimal usedFraction) => usedFraction switch
    {
        >= RedAt => BudgetLevel.Red,
        >= AmberAt => BudgetLevel.Amber,
        _ => BudgetLevel.Ok,
    };
}
