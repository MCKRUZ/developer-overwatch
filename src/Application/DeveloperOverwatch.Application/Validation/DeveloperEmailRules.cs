namespace DeveloperOverwatch.Application.Validation;

using DeveloperOverwatch.Domain.Common;
using FluentValidation;

internal static class DeveloperEmailRules
{
    /// <summary>A developer email as stored: present, an address, and within the column. Apply it to the trimmed
    /// value, since that is what the entity keeps.</summary>
    public static IRuleBuilderOptions<T, string?> ValidDeveloperEmail<T>(this IRuleBuilder<T, string?> rule) =>
        rule.NotEmpty().MaximumLength(EmailLimits.MaxLength).EmailAddress();
}
