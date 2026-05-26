namespace DeveloperOverwatch.Infrastructure.Governance;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
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
        if (currentViolations.Count == 0)
            return [];

        var window = TimeSpan.FromHours(_config.DriftWindowHours);
        var recentViolations = await repository.GetRecentAsync(window, ct);

        var driftViolations = new List<GovernanceViolation>();
        var checkedRules = new HashSet<string>();

        foreach (var violation in currentViolations)
        {
            if (!checkedRules.Add(violation.RuleId))
                continue;

            var unresolvedCount = recentViolations
                .Count(v => v.RuleId == violation.RuleId && !v.Resolved);

            var totalCount = unresolvedCount + currentViolations.Count(v => v.RuleId == violation.RuleId);

            if (totalCount >= _config.DriftThreshold)
            {
                _logger.LogWarning("Drift detected: rule '{RuleId}' violated {Count} times in {Window}h",
                    violation.RuleId, totalCount, _config.DriftWindowHours);

                driftViolations.Add(new GovernanceViolation
                {
                    Id = Guid.NewGuid().ToString(),
                    SessionId = sessionId,
                    RuleId = $"drift:{violation.RuleId}",
                    Severity = ViolationSeverity.Warning,
                    Message = $"Drift detected: rule '{violation.RuleId}' violated {totalCount} times in the last {_config.DriftWindowHours}h",
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        return driftViolations;
    }
}
