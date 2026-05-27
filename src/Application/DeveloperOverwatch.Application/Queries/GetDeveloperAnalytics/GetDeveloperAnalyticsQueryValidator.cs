namespace DeveloperOverwatch.Application.Queries.GetDeveloperAnalytics;

using FluentValidation;

public sealed class GetDeveloperAnalyticsQueryValidator : AbstractValidator<GetDeveloperAnalyticsQuery>
{
    public GetDeveloperAnalyticsQueryValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}
