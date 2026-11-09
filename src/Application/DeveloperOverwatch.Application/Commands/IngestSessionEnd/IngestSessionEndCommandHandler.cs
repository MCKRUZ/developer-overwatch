namespace DeveloperOverwatch.Application.Commands.IngestSessionEnd;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class IngestSessionEndCommandHandler
    : IRequestHandler<IngestSessionEndCommand, Result<string>>
{
    private readonly ISessionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public IngestSessionEndCommandHandler(ISessionRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<string>> Handle(
        IngestSessionEndCommand request,
        CancellationToken cancellationToken)
    {
        var session = await _repository.GetByIdAsync(request.SessionId, cancellationToken);
        if (session is null)
            return Result<string>.Fail($"Session {request.SessionId} not found");

        // The Agent delivers at-least-once, and backfill may have closed an idle session first, so an
        // end for a session that is already over succeeds without changing it. Not-found is then this
        // handler's only failure, which the controller relies on.
        if (session.Status != SessionStatus.Active)
            return Result<string>.Success(session.Id);

        session.Status = SessionStatus.Completed;
        session.EndedAt = request.EndedAt;
        session.SummaryJson = request.SummaryJson;

        await _repository.UpdateAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<string>.Success(session.Id);
    }
}
