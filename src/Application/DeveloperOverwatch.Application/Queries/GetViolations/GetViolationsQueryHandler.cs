namespace DeveloperOverwatch.Application.Queries.GetViolations;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetViolationsQueryHandler
    : IRequestHandler<GetViolationsQuery, Result<IReadOnlyList<GovernanceViolation>>>
{
    private readonly IGovernanceViolationRepository _repository;

    public GetViolationsQueryHandler(IGovernanceViolationRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<GovernanceViolation>>> Handle(
        GetViolationsQuery request, CancellationToken cancellationToken)
    {
        var violations = await _repository.GetBySessionIdAsync(
            request.SessionId, request.Severity, cancellationToken);
        return Result<IReadOnlyList<GovernanceViolation>>.Success(violations);
    }
}
