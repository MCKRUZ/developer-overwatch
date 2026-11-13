namespace DeveloperOverwatch.Domain.Entities;

using DeveloperOverwatch.Domain.Enums;

/// <summary>
/// A spending limit in API-equivalent US dollars for the team or one developer over a day or a month.
/// At most one budget exists per subject and period; <see cref="SubjectKey"/> carries that rule because
/// SQLite treats NULLs in a unique index as distinct, so an index over the nullable email would let two
/// team budgets through.
/// </summary>
public sealed class Budget
{
    /// <summary>The <see cref="SubjectKey"/> of every team budget. Developer emails are validated as
    /// email addresses, so none can equal it.</summary>
    public const string TeamSubjectKey = "team";

    private Budget() { }

    public Guid Id { get; private set; }
    public BudgetScope Scope { get; private set; }

    /// <summary>The trimmed developer email for a developer budget; null for the team.</summary>
    public string? DeveloperEmail { get; private set; }

    /// <summary><see cref="TeamSubjectKey"/> for the team, else the trimmed developer email.</summary>
    public string SubjectKey { get; private set; } = string.Empty;

    public BudgetPeriod Period { get; private set; }
    public decimal AmountUsd { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public static Budget Create(
        BudgetScope scope, string? developerEmail, BudgetPeriod period, decimal amountUsd, DateTime nowUtc)
    {
        var email = NormalizeEmail(scope, developerEmail);
        ThrowIfNotPositive(amountUsd);
        return new Budget
        {
            Id = Guid.NewGuid(),
            Scope = scope,
            DeveloperEmail = email,
            SubjectKey = email ?? TeamSubjectKey,
            Period = period,
            AmountUsd = amountUsd,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        };
    }

    /// <summary>The <see cref="SubjectKey"/> a budget for this scope and email would get, for looking one up.</summary>
    public static string SubjectKeyFor(BudgetScope scope, string? developerEmail) =>
        NormalizeEmail(scope, developerEmail) ?? TeamSubjectKey;

    public void ChangeAmount(decimal amountUsd, DateTime nowUtc)
    {
        ThrowIfNotPositive(amountUsd);
        AmountUsd = amountUsd;
        UpdatedAt = nowUtc;
    }

    private static string? NormalizeEmail(BudgetScope scope, string? developerEmail)
    {
        if (scope == BudgetScope.Team)
        {
            if (developerEmail is not null)
                throw new ArgumentException("A team budget has no developer email.", nameof(developerEmail));
            return null;
        }

        if (string.IsNullOrWhiteSpace(developerEmail))
            throw new ArgumentException("A developer budget needs the developer's email.", nameof(developerEmail));
        return developerEmail.Trim();
    }

    private static void ThrowIfNotPositive(decimal amountUsd) =>
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amountUsd);
}
