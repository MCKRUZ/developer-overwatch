namespace DeveloperOverwatch.Application.Commands.UpdateBudget;

using DeveloperOverwatch.Application.Validation;
using FluentValidation;

public sealed class UpdateBudgetCommandValidator : AbstractValidator<UpdateBudgetCommand>
{
    public UpdateBudgetCommandValidator()
    {
        RuleFor(x => x.AmountUsd).ValidBudgetAmount();
    }
}
