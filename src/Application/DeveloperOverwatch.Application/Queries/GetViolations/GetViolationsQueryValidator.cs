using FluentValidation;

namespace DeveloperOverwatch.Application.Queries.GetViolations;

public sealed class GetViolationsQueryValidator : AbstractValidator<GetViolationsQuery>
{
    public GetViolationsQueryValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
    }
}
