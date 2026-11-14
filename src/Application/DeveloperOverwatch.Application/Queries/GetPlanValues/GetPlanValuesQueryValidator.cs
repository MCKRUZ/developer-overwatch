namespace DeveloperOverwatch.Application.Queries.GetPlanValues;

using DeveloperOverwatch.Application.Validation;
using FluentValidation;

public sealed class GetPlanValuesQueryValidator : AbstractValidator<GetPlanValuesQuery>
{
    public GetPlanValuesQueryValidator() => RuleFor(x => x.TimeZone).ValidTzParameter();
}
