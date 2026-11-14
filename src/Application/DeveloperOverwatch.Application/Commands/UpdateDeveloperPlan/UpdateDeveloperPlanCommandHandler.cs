namespace DeveloperOverwatch.Application.Commands.UpdateDeveloperPlan;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class UpdateDeveloperPlanCommandHandler
    : IRequestHandler<UpdateDeveloperPlanCommand, Result<DeveloperPlanDto>>
{
    private readonly IDeveloperPlanRepository _plans;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public UpdateDeveloperPlanCommandHandler(IDeveloperPlanRepository plans, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _plans = plans;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task<Result<DeveloperPlanDto>> Handle(
        UpdateDeveloperPlanCommand request, CancellationToken cancellationToken)
    {
        var plan = await _plans.GetByIdAsync(request.Id, cancellationToken);
        if (plan is null)
            return Result<DeveloperPlanDto>.Fail(ErrorCodes.NotFound);

        plan.Update(request.PlanName, request.MonthlyPriceUsd, request.WindowLimitUsd, _time.GetUtcNow().UtcDateTime);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<DeveloperPlanDto>.Success(DeveloperPlanDto.From(plan));
    }
}
