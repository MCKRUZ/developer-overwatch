namespace DeveloperOverwatch.Application.Queries.GetBudgetStatuses;

using DeveloperOverwatch.Application.Validation;
using FluentValidation;

public sealed class GetBudgetStatusesQueryValidator : AbstractValidator<GetBudgetStatusesQuery>
{
    public GetBudgetStatusesQueryValidator() => RuleFor(x => x.TimeZone).ValidTzParameter();
}
