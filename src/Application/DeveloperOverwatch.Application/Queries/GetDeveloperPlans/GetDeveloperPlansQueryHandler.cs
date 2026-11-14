namespace DeveloperOverwatch.Application.Queries.GetDeveloperPlans;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetDeveloperPlansQueryHandler
    : IRequestHandler<GetDeveloperPlansQuery, Result<IReadOnlyList<DeveloperPlanDto>>>
{
    private readonly IDeveloperPlanRepository _plans;

    public GetDeveloperPlansQueryHandler(IDeveloperPlanRepository plans) => _plans = plans;

    public async Task<Result<IReadOnlyList<DeveloperPlanDto>>> Handle(
        GetDeveloperPlansQuery request, CancellationToken cancellationToken)
    {
        var plans = await _plans.GetAllAsync(cancellationToken);
        return Result<IReadOnlyList<DeveloperPlanDto>>.Success(plans.Select(DeveloperPlanDto.From).ToList());
    }
}
