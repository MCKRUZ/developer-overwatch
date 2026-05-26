namespace DeveloperOverwatch.Infrastructure.Governance;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class DriftDetector
{
    private readonly GovernanceConfig _config;
    private readonly ILogger<DriftDetector> _logger;

    public DriftDetector(IOptions<GovernanceConfig> config, ILogger<DriftDetector> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<GovernanceViolation>> DetectAsync(
        string sessionId,
        IReadOnlyList<GovernanceViolation> currentViolations,
        IGovernanceViolationRepository repository,
        CancellationToken ct = default)
    {
        return [];
    }
}
