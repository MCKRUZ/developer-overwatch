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

            var ruleViolations = recentViolations
                .Where(v => v.RuleId == violation.RuleId && !v.Resolved)
                .Concat(currentViolations.Where(v => v.RuleId == violation.RuleId))
                .ToList();

            var ewma = ComputeEwma(ruleViolations);

            if (ewma >= _config.DriftThreshold)
            {
                _logger.LogWarning(
                    "Drift detected: rule '{RuleId}' EWMA={Ewma:F2} (threshold: {Threshold}) over {Window}h",
                    violation.RuleId, ewma, _config.DriftThreshold, _config.DriftWindowHours);

                driftViolations.Add(new GovernanceViolation
                {
                    Id = Guid.NewGuid().ToString(),
                    SessionId = sessionId,
                    RuleId = $"drift:{violation.RuleId}",
                    Severity = ViolationSeverity.Warning,
                    Message = $"Drift detected: rule '{violation.RuleId}' violated {ruleViolations.Count} times in the last {_config.DriftWindowHours}h (EWMA: {ewma:F2})",
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        return driftViolations;
    }

    private double ComputeEwma(IReadOnlyList<GovernanceViolation> violations)
    {
        if (violations.Count == 0)
            return 0;

        var now = DateTime.UtcNow;
        var bucketSize = TimeSpan.FromHours(_config.DriftBucketHours);
        var bucketCount = (int)Math.Ceiling((double)_config.DriftWindowHours / _config.DriftBucketHours);
        var alpha = _config.DriftAlpha;

        // Count violations per bucket (oldest first)
        var buckets = new int[bucketCount];
        foreach (var v in violations)
        {
            var age = now - v.CreatedAt;
            var bucketIndex = (int)(age.TotalHours / _config.DriftBucketHours);
            if (bucketIndex >= 0 && bucketIndex < bucketCount)
                buckets[bucketIndex] = buckets[bucketIndex] + 1;
        }

        // EWMA from oldest to newest (bucket[last] is oldest, bucket[0] is most recent)
        var ewma = 0.0;
        for (var i = bucketCount - 1; i >= 0; i--)
            ewma = alpha * buckets[i] + (1 - alpha) * ewma;

        return ewma;
    }
}
