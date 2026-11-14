namespace DeveloperOverwatch.Application.Commands.UpdateDeveloperPlan;

using DeveloperOverwatch.Application.Validation;
using FluentValidation;

public sealed class UpdateDeveloperPlanCommandValidator : AbstractValidator<UpdateDeveloperPlanCommand>
{
    public UpdateDeveloperPlanCommandValidator()
    {
        RuleFor(x => x.PlanName == null ? null : x.PlanName.Trim()).ValidPlanName()
            .OverridePropertyName(nameof(UpdateDeveloperPlanCommand.PlanName));
        RuleFor(x => x.MonthlyPriceUsd).ValidMonthlyPrice();
        RuleFor(x => x.WindowLimitUsd).ValidWindowLimit();
    }
}
