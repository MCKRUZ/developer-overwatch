namespace DeveloperOverwatch.Application.Commands.IngestSessionStart;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class IngestSessionStartCommandHandler
    : IRequestHandler<IngestSessionStartCommand, Result<IngestOutcome>>
{
    private readonly ISessionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public IngestSessionStartCommandHandler(ISessionRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IngestOutcome>> Handle(
        IngestSessionStartCommand request,
        CancellationToken cancellationToken)
    {
        // A redelivered start must change nothing. Without this, a resent start for a session that
        // has since ended would reopen it. Checked before any write, including the gap fills below.
        if (request.EventId is { } eventId
            && await _repository.IsAgentEventProcessedAsync(eventId, cancellationToken))
            return Result<IngestOutcome>.Success(IngestOutcome.Duplicate);

        var existing = await _repository.GetByIdAsync(request.SessionId, cancellationToken);
        if (existing is not null)
        {
            ApplyToExisting(existing, request);
            await _repository.UpdateAsync(existing, cancellationToken);
        }
        else
        {
            await _repository.AddAsync(NewSession(request), cancellationToken);
        }

        if (request.EventId is not null)
        {
            await _repository.AddProcessedAgentEventAsync(new ProcessedAgentEvent
            {
                EventId = request.EventId,
                SessionId = request.SessionId,
                Kind = ProcessedAgentEventKind.SessionStart,
                ProcessedAt = DateTime.UtcNow
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<IngestOutcome>.Success(IngestOutcome.Accepted);
    }

    private static void ApplyToExisting(Session existing, IngestSessionStartCommand request)
    {
        if (request.IsBackfill)
        {
            // Backfill re-sends starts for past sessions, often ones that already ended. It
            // only fills gaps and never reopens or re-times the session.
            if (string.IsNullOrWhiteSpace(existing.Title) && !string.IsNullOrWhiteSpace(request.Title))
                existing.Title = request.Title;
        }
        else
        {
            // The same Claude Code session can fire SessionStart again (e.g. `/resume`, `/clear`,
            // `/compact`) — reactivate the existing session rather than rejecting as a duplicate.
            existing.Status = SessionStatus.Active;
            existing.EndedAt = null;
            existing.LastActivityAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(request.Title))
                existing.Title = request.Title;
        }
        existing.MachineName ??= request.MachineName;
        existing.ProjectKey ??= request.ProjectKey;
    }

    private static Session NewSession(IngestSessionStartCommand request) => new()
    {
        Id = request.SessionId,
        DeveloperEmail = request.DeveloperEmail,
        DeveloperName = request.DeveloperName,
        RepoPath = request.RepoPath,
        Title = request.Title,
        Status = SessionStatus.Active,
        StartedAt = request.StartedAt,
        CreatedAt = DateTime.UtcNow,
        // A backfilled session happened in the past; the SessionEnd that backfill also sends closes it.
        LastActivityAt = request.IsBackfill ? request.StartedAt : DateTime.UtcNow,
        Agent = string.IsNullOrWhiteSpace(request.Agent) ? Session.DefaultAgent : request.Agent,
        MachineName = request.MachineName,
        ProjectKey = request.ProjectKey
    };
}
