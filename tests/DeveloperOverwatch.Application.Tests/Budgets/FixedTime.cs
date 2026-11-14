namespace DeveloperOverwatch.Application.Tests.Budgets;

/// <summary>A clock stopped at one UTC moment, so period edges and forecasts are exact.</summary>
internal sealed class FixedTime(DateTime nowUtc) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(nowUtc);
}
