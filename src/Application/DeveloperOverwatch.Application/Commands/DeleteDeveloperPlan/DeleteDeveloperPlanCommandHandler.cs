namespace DeveloperOverwatch.Application.Commands.DeleteDeveloperPlan;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class DeleteDeveloperPlanCommandHandler : IRequestHandler<DeleteDeveloperPlanCommand, Result<Guid>>
{
    private readonly IDeveloperPlanRepository _plans;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteDeveloperPlanCommandHandler(IDeveloperPlanRepository plans, IUnitOfWork unitOfWork)
    {
        _plans = plans;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(DeleteDeveloperPlanCommand request, CancellationToken cancellationToken)
    {
        var plan = await _plans.GetByIdAsync(request.Id, cancellationToken);
        if (plan is null)
            return Result<Guid>.Fail(ErrorCodes.NotFound);

        _plans.Remove(plan);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(plan.Id);
    }
}
