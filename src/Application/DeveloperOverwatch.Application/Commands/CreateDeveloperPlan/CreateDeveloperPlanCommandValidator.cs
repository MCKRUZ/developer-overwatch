namespace DeveloperOverwatch.Application.Commands.CreateDeveloperPlan;

using DeveloperOverwatch.Application.Validation;
using FluentValidation;

public sealed class CreateDeveloperPlanCommandValidator : AbstractValidator<CreateDeveloperPlanCommand>
{
    public CreateDeveloperPlanCommandValidator()
    {
        RuleFor(x => x.DeveloperEmail == null ? null : x.DeveloperEmail.Trim()).ValidDeveloperEmail()
            .OverridePropertyName(nameof(CreateDeveloperPlanCommand.DeveloperEmail));
        RuleFor(x => x.PlanName == null ? null : x.PlanName.Trim()).ValidPlanName()
            .OverridePropertyName(nameof(CreateDeveloperPlanCommand.PlanName));
        RuleFor(x => x.MonthlyPriceUsd).ValidMonthlyPrice();
        RuleFor(x => x.WindowLimitUsd).ValidWindowLimit();
    }
}
