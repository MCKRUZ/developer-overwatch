namespace DeveloperOverwatch.Application.Queries.GetUsageSummary;

using FluentValidation;

public sealed class GetUsageSummaryQueryValidator : AbstractValidator<GetUsageSummaryQuery>
{
    /// <summary>Longest period, counting both end days: a leap year.</summary>
    public const int MaxDays = 366;

    public GetUsageSummaryQueryValidator()
    {
        RuleFor(x => x.From).NotNull();
        RuleFor(x => x.To).NotNull();
        RuleFor(x => x)
            .Must(x => x.From <= x.To)
            .When(x => x.From is not null && x.To is not null)
            .OverridePropertyName(nameof(GetUsageSummaryQuery.From))
            .WithMessage("'from' must not be after 'to'.");
        RuleFor(x => x)
            .Must(x => x.To!.Value.DayNumber - x.From!.Value.DayNumber + 1 <= MaxDays)
            .When(x => x.From is not null && x.To is not null && x.From <= x.To)
            .OverridePropertyName(nameof(GetUsageSummaryQuery.To))
            .WithMessage($"The period must be at most {MaxDays} days.");
        RuleFor(x => x.Granularity)
            .Must(UsageCalendar.IsGranularity)
            .WithMessage("'granularity' must be day, week or month.");
        RuleFor(x => x.TimeZone)
            .Must(id => UsageCalendar.TryFindZone(id, out _))
            .WithMessage("'timeZone' must be a known IANA time zone id.");
    }
}
