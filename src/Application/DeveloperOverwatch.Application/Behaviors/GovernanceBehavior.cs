namespace DeveloperOverwatch.Application.Behaviors;

using DeveloperOverwatch.Application.Commands.IngestSessionEnd;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

public sealed class GovernanceBehavior
    : IPipelineBehavior<IngestSessionEndCommand, Result<string>>
{
    private readonly IGovernanceEngine _engine;
    private readonly IOverwatchNotifier _notifier;
    private readonly ILogger<GovernanceBehavior> _logger;

    public GovernanceBehavior(
        IGovernanceEngine engine,
        IOverwatchNotifier notifier,
        ILogger<GovernanceBehavior> logger)
    {
        _engine = engine;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<Result<string>> Handle(
        IngestSessionEndCommand request,
        RequestHandlerDelegate<Result<string>> next,
        CancellationToken cancellationToken)
    {
        var result = await next(cancellationToken);

        if (!result.IsSuccess)
            return result;

        try
        {
            var violations = await _engine.EvaluateAsync(request.SessionId, cancellationToken);
            if (violations.Count > 0)
            {
                _logger.LogInformation("Governance: {Count} violations for session {SessionId}",
                    violations.Count, request.SessionId);

                foreach (var violation in violations)
                    await _notifier.ViolationCreatedAsync(violation, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Governance evaluation failed for session {SessionId} — non-blocking",
                request.SessionId);
        }

        return result;
    }
}
