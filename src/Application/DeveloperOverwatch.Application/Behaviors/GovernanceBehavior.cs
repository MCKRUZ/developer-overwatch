namespace DeveloperOverwatch.Application.Behaviors;

using DeveloperOverwatch.Application.Commands.IngestSessionEnd;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

/// <summary>Evaluates governance when a session end actually closes an active session. A redelivered or
/// no-op end succeeds too, and does not raise the session's violations again. A session resumed after it
/// closed and then closed again is evaluated again, adding that close's violations.</summary>
/// <remarks>Evaluation is not tied to the request: once the session has closed, a redelivered end is a
/// duplicate, so governance cut short by a client that stopped waiting would never run.</remarks>
public sealed class GovernanceBehavior
    : IPipelineBehavior<IngestSessionEndCommand, Result<IngestSessionEndResult>>
{
    private static readonly TimeSpan EvaluationTimeout = TimeSpan.FromMinutes(2);

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

    public async Task<Result<IngestSessionEndResult>> Handle(
        IngestSessionEndCommand request,
        RequestHandlerDelegate<Result<IngestSessionEndResult>> next,
        CancellationToken cancellationToken)
    {
        var result = await next(cancellationToken);

        if (!result.IsSuccess || result.Value?.ClosedSession != true)
            return result;

        using var evaluation = new CancellationTokenSource(EvaluationTimeout);
        try
        {
            var violations = await _engine.EvaluateAsync(request.SessionId, evaluation.Token);
            if (violations.Count > 0)
            {
                _logger.LogInformation("Governance: {Count} violations for session {SessionId}",
                    violations.Count, request.SessionId);

                foreach (var violation in violations)
                    await _notifier.ViolationCreatedAsync(violation, evaluation.Token);
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
