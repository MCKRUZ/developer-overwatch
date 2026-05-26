namespace DeveloperOverwatch.Infrastructure.Governance;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.Extensions.Logging;

public sealed class GovernanceEngine : IGovernanceEngine
{
    private readonly ISessionRepository _sessionRepo;
    private readonly IGovernanceViolationRepository _violationRepo;
    private readonly PolicyLoader _policyLoader;
    private readonly PatternRuleEvaluator _patternEvaluator;
    private readonly DriftDetector _driftDetector;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<GovernanceEngine> _logger;

    public GovernanceEngine(
        ISessionRepository sessionRepo,
        IGovernanceViolationRepository violationRepo,
        PolicyLoader policyLoader,
        PatternRuleEvaluator patternEvaluator,
        DriftDetector driftDetector,
        IUnitOfWork unitOfWork,
        ILogger<GovernanceEngine> logger)
    {
        _sessionRepo = sessionRepo;
        _violationRepo = violationRepo;
        _policyLoader = policyLoader;
        _patternEvaluator = patternEvaluator;
        _driftDetector = driftDetector;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<IReadOnlyList<GovernanceViolation>> EvaluateAsync(string sessionId, CancellationToken ct = default)
    {
        var session = await _sessionRepo.GetByIdAsync(sessionId, ct);
        if (session is null)
        {
            _logger.LogWarning("Governance: session {SessionId} not found", sessionId);
            return [];
        }

        var policies = _policyLoader.LoadPolicies();
        if (policies.Count == 0)
        {
            _logger.LogDebug("No governance policies loaded, skipping evaluation");
            return [];
        }

        var allViolations = new List<GovernanceViolation>();

        foreach (var policy in policies)
        {
            switch (policy.Type)
            {
                case PolicyType.Pattern:
                    var violations = _patternEvaluator.Evaluate(policy, session);
                    allViolations.AddRange(violations);
                    break;

                case PolicyType.Semantic:
                    _logger.LogDebug("Semantic rule '{RuleId}' skipped — LLM integration not configured", policy.Id);
                    break;
            }
        }

        var driftViolations = await _driftDetector.DetectAsync(sessionId, allViolations, _violationRepo, ct);
        allViolations.AddRange(driftViolations);

        if (allViolations.Count > 0)
        {
            await _violationRepo.AddRangeAsync(allViolations, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            _logger.LogInformation("Governance: {Count} violations found for session {SessionId}",
                allViolations.Count, sessionId);
        }

        return allViolations;
    }
}
