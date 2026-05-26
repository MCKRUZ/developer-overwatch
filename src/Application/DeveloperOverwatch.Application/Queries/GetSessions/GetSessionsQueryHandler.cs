namespace DeveloperOverwatch.Application.Queries.GetSessions;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetSessionsQueryHandler
    : IRequestHandler<GetSessionsQuery, Result<IReadOnlyList<Session>>>
{
    private readonly ISessionRepository _repository;

    public GetSessionsQueryHandler(ISessionRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<Session>>> Handle(
        GetSessionsQuery request, CancellationToken cancellationToken)
    {
        var sessions = await _repository.GetAllAsync(
            request.DeveloperEmail, request.Repo, cancellationToken);
        return Result<IReadOnlyList<Session>>.Success(sessions);
    }
}
