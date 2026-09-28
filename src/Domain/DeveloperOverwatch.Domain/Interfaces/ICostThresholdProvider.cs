namespace DeveloperOverwatch.Domain.Interfaces;

/// <summary>Configured daily spend threshold used to flag high-cost days in the dashboard.</summary>
public interface ICostThresholdProvider
{
    decimal DailyThresholdUsd { get; }
}
