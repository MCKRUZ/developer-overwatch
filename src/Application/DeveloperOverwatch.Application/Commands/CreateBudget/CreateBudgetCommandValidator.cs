namespace DeveloperOverwatch.Application.Commands.CreateBudget;

using DeveloperOverwatch.Application.Validation;
using DeveloperOverwatch.Domain.Enums;
using FluentValidation;

public sealed class CreateBudgetCommandValidator : AbstractValidator<CreateBudgetCommand>
{
    public CreateBudgetCommandValidator()
    {
        RuleFor(x => x.Scope).IsInEnum();
        RuleFor(x => x.Period).IsInEnum();
        RuleFor(x => x.AmountUsd).ValidBudgetAmount();

        // Null, not empty: the entity refuses any email on a team budget, "" included.
        RuleFor(x => x.DeveloperEmail).Null()
            .WithMessage("A team budget has no developer email.")
            .When(x => x.Scope == BudgetScope.Team);
        RuleFor(x => x.DeveloperEmail == null ? null : x.DeveloperEmail.Trim()).ValidDeveloperEmail()
            .OverridePropertyName(nameof(CreateBudgetCommand.DeveloperEmail))
            .When(x => x.Scope == BudgetScope.Developer);
    }
}
