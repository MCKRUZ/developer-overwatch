namespace DeveloperOverwatch.Application.Validation;

using DeveloperOverwatch.Application.Queries.GetUsageSummary;
using FluentValidation;

internal static class TimeZoneRules
{
    /// <summary>The viewer's zone from the <c>tz</c> query parameter: present and resolvable. Reported under
    /// <c>tz</c>, the name the caller sent, rather than the query's property name.</summary>
    public static IRuleBuilderOptions<T, string?> ValidTzParameter<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(id => UsageCalendar.TryFindZone(id, out _))
            .OverridePropertyName("tz")
            .WithMessage("'tz' must be a known IANA time zone id.");
}
