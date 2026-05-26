namespace DeveloperOverwatch.Application.Commands.IngestSessionStart;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class IngestSessionStartCommandHandler
    : IRequestHandler<IngestSessionStartCommand, Result<string>>
{
    private readonly ISessionRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public IngestSessionStartCommandHandler(ISessionRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<string>> Handle(
        IngestSessionStartCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await _repository.GetByIdAsync(request.SessionId, cancellationToken);
        if (existing is not null)
            return Result<string>.Fail($"Session {request.SessionId} already exists");

        var session = new Session
        {
            Id = request.SessionId,
            DeveloperEmail = request.DeveloperEmail,
            DeveloperName = request.DeveloperName,
            RepoPath = request.RepoPath,
            Status = SessionStatus.Active,
            StartedAt = request.StartedAt,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.AddAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<string>.Success(session.Id);
    }
}
