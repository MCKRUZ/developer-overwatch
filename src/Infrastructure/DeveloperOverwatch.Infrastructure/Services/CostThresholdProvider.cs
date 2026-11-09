namespace DeveloperOverwatch.Infrastructure.Services;

using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.Extensions.Options;

public sealed class CostAlertsConfig
{
    public decimal DailyThresholdUsd { get; set; } = 50.00m;
}

public sealed class CostThresholdProvider : ICostThresholdProvider
{
    public CostThresholdProvider(IOptions<CostAlertsConfig> config)
    {
        DailyThresholdUsd = config.Value.DailyThresholdUsd;
    }

    public decimal DailyThresholdUsd { get; }
}
