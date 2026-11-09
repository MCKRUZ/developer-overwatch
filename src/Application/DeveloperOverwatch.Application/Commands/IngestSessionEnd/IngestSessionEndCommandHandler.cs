namespace DeveloperOverwatch.Application.Commands.IngestSessionEnd;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class IngestSessionEndCommandHandler
    : IRequestHandler<IngestSessionEndCommand, Result<IngestOutcome>>
{
    private readonly ISessionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public IngestSessionEndCommandHandler(ISessionRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<IngestOutcome>> Handle(
        IngestSessionEndCommand request,
        CancellationToken cancellationToken)
    {
        var session = await _repository.GetByIdAsync(request.SessionId, cancellationToken);
        if (session is null)
            return Result<IngestOutcome>.Fail($"Session {request.SessionId} not found");

        if (request.EventId is { } eventId
            && await _repository.IsAgentEventProcessedAsync(eventId, cancellationToken))
            return Result<IngestOutcome>.Success(IngestOutcome.Duplicate);

        // The Agent delivers at-least-once, and backfill may have closed an idle session first, so an
        // end for a session that is already over succeeds without changing it. Not-found is then this
        // handler's only failure, which the controller relies on.
        if (session.Status != SessionStatus.Active && request.EventId is null)
            return Result<IngestOutcome>.Success(IngestOutcome.Accepted);

        if (session.Status == SessionStatus.Active)
        {
            session.Status = SessionStatus.Completed;
            session.EndedAt = request.EndedAt;
            session.SummaryJson = request.SummaryJson;
            await _repository.UpdateAsync(session, cancellationToken);
        }

        if (request.EventId is not null)
        {
            await _repository.AddProcessedAgentEventAsync(new ProcessedAgentEvent
            {
                EventId = request.EventId,
                SessionId = request.SessionId,
                Kind = ProcessedAgentEventKind.SessionEnd,
                ProcessedAt = DateTime.UtcNow
            }, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<IngestOutcome>.Success(IngestOutcome.Accepted);
    }
}
