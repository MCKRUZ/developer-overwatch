namespace DeveloperOverwatch.Application.Commands.UpdateBudget;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class UpdateBudgetCommandHandler : IRequestHandler<UpdateBudgetCommand, Result<BudgetDto>>
{
    private readonly IBudgetRepository _budgets;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public UpdateBudgetCommandHandler(IBudgetRepository budgets, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _budgets = budgets;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task<Result<BudgetDto>> Handle(UpdateBudgetCommand request, CancellationToken cancellationToken)
    {
        var budget = await _budgets.GetByIdAsync(request.Id, cancellationToken);
        if (budget is null)
            return Result<BudgetDto>.Fail(ErrorCodes.NotFound);

        budget.ChangeAmount(request.AmountUsd, _time.GetUtcNow().UtcDateTime);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<BudgetDto>.Success(BudgetDto.From(budget));
    }
}
