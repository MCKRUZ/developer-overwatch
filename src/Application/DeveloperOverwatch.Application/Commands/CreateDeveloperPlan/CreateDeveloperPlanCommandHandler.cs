namespace DeveloperOverwatch.Application.Commands.CreateDeveloperPlan;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class CreateDeveloperPlanCommandHandler
    : IRequestHandler<CreateDeveloperPlanCommand, Result<DeveloperPlanDto>>
{
    private readonly IDeveloperPlanRepository _plans;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public CreateDeveloperPlanCommandHandler(IDeveloperPlanRepository plans, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _plans = plans;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task<Result<DeveloperPlanDto>> Handle(
        CreateDeveloperPlanCommand request, CancellationToken cancellationToken)
    {
        if (await _plans.FindByEmailAsync(request.DeveloperEmail.Trim(), cancellationToken) is not null)
            return Result<DeveloperPlanDto>.Fail(ErrorCodes.AlreadyExists);

        var plan = DeveloperPlan.Create(
            request.DeveloperEmail, request.PlanName, request.MonthlyPriceUsd, request.WindowLimitUsd,
            _time.GetUtcNow().UtcDateTime);
        await _plans.AddAsync(plan, cancellationToken);

        // The check above can race another create; the unique index settles it.
        return await _unitOfWork.SaveChangesDetectingDuplicateAsync(cancellationToken) == SaveOutcome.Duplicate
            ? Result<DeveloperPlanDto>.Fail(ErrorCodes.AlreadyExists)
            : Result<DeveloperPlanDto>.Success(DeveloperPlanDto.From(plan));
    }
}
