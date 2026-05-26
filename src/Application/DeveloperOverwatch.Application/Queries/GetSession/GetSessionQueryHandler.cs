namespace DeveloperOverwatch.Application.Queries.GetSession;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetSessionQueryHandler
    : IRequestHandler<GetSessionQuery, Result<Session>>
{
    private readonly ISessionRepository _repository;

    public GetSessionQueryHandler(ISessionRepository repository) => _repository = repository;

    public async Task<Result<Session>> Handle(
        GetSessionQuery request, CancellationToken cancellationToken)
    {
        var session = await _repository.GetByIdAsync(request.SessionId, cancellationToken);
        return session is null
            ? Result<Session>.Fail($"Session {request.SessionId} not found")
            : Result<Session>.Success(session);
    }
}
