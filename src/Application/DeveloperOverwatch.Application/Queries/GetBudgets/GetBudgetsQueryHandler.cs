namespace DeveloperOverwatch.Application.Queries.GetBudgets;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class GetBudgetsQueryHandler : IRequestHandler<GetBudgetsQuery, Result<IReadOnlyList<BudgetDto>>>
{
    private readonly IBudgetRepository _budgets;

    public GetBudgetsQueryHandler(IBudgetRepository budgets) => _budgets = budgets;

    public async Task<Result<IReadOnlyList<BudgetDto>>> Handle(GetBudgetsQuery request, CancellationToken cancellationToken)
    {
        var budgets = await _budgets.GetAllAsync(cancellationToken);
        return Result<IReadOnlyList<BudgetDto>>.Success(budgets.Select(BudgetDto.From).ToList());
    }
}
