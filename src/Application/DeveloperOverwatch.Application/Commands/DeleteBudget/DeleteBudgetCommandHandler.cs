namespace DeveloperOverwatch.Application.Commands.DeleteBudget;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class DeleteBudgetCommandHandler : IRequestHandler<DeleteBudgetCommand, Result<Guid>>
{
    private readonly IBudgetRepository _budgets;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteBudgetCommandHandler(IBudgetRepository budgets, IUnitOfWork unitOfWork)
    {
        _budgets = budgets;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(DeleteBudgetCommand request, CancellationToken cancellationToken)
    {
        var budget = await _budgets.GetByIdAsync(request.Id, cancellationToken);
        if (budget is null)
            return Result<Guid>.Fail(ErrorCodes.NotFound);

        _budgets.Remove(budget);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(budget.Id);
    }
}
