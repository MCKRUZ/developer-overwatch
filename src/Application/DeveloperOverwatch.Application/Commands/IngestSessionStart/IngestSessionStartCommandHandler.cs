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
        {
            // The same Claude Code session can fire SessionStart again (e.g. `/resume`, `/clear`,
            // `/compact`) — reactivate the existing session rather than rejecting as a duplicate.
            existing.Status = SessionStatus.Active;
            existing.EndedAt = null;
            existing.LastActivityAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(request.Title))
                existing.Title = request.Title;
            await _repository.UpdateAsync(existing, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<string>.Success(existing.Id);
        }

        var session = new Session
        {
            Id = request.SessionId,
            DeveloperEmail = request.DeveloperEmail,
            DeveloperName = request.DeveloperName,
            RepoPath = request.RepoPath,
            Title = request.Title,
            Status = SessionStatus.Active,
            StartedAt = request.StartedAt,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.AddAsync(session, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<string>.Success(session.Id);
    }
}
