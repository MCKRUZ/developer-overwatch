namespace DeveloperOverwatch.Infrastructure.Services;

using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.Extensions.Options;

public sealed class SessionActivityConfig
{
    public int StaleAfterMinutes { get; set; } = 30;
}

public sealed class SessionStaleThresholdProvider : ISessionStaleThresholdProvider
{
    public SessionStaleThresholdProvider(IOptions<SessionActivityConfig> config)
    {
        StaleAfterMinutes = config.Value.StaleAfterMinutes;
    }

    public int StaleAfterMinutes { get; }
}
