namespace DeveloperOverwatch.Application.Commands.CreateBudget;

using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using MediatR;

public sealed class CreateBudgetCommandHandler : IRequestHandler<CreateBudgetCommand, Result<BudgetDto>>
{
    private readonly IBudgetRepository _budgets;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public CreateBudgetCommandHandler(IBudgetRepository budgets, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _budgets = budgets;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task<Result<BudgetDto>> Handle(CreateBudgetCommand request, CancellationToken cancellationToken)
    {
        var subjectKey = Budget.SubjectKeyFor(request.Scope, request.DeveloperEmail);
        if (await _budgets.FindAsync(subjectKey, request.Period, cancellationToken) is not null)
            return Result<BudgetDto>.Fail(ErrorCodes.AlreadyExists);

        var budget = Budget.Create(
            request.Scope, request.DeveloperEmail, request.Period, request.AmountUsd, _time.GetUtcNow().UtcDateTime);
        await _budgets.AddAsync(budget, cancellationToken);

        // The check above can race another create; the unique index settles it.
        return await _unitOfWork.SaveChangesDetectingDuplicateAsync(cancellationToken) == SaveOutcome.Duplicate
            ? Result<BudgetDto>.Fail(ErrorCodes.AlreadyExists)
            : Result<BudgetDto>.Success(BudgetDto.From(budget));
    }
}
