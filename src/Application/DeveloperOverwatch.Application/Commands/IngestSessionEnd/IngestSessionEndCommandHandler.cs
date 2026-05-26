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

        if (session.Status != SessionStatus.Active)
            return Result<string>.Fail($"Session {request.SessionId} is not active");

        session.Status = SessionStatus.Completed;
        session.EndedAt = request.EndedAt;
        session.SummaryJson = request.SummaryJson;

        await _repository.UpdateAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<string>.Success(session.Id);
    }
}
