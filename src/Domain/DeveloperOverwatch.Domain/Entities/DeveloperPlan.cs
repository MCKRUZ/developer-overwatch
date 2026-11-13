namespace DeveloperOverwatch.Domain.Entities;

/// <summary>
/// The subscription a lead says a developer is on: what it costs per month and, optionally, how many
/// API-equivalent US dollars of usage one 5-hour window allows. Entered by hand and shown as an estimate.
/// One plan per developer email.
/// </summary>
public sealed class DeveloperPlan
{
    /// <summary>The longest plan name stored, after trimming.</summary>
    public const int PlanNameMaxLength = 100;

    private DeveloperPlan() { }

    public Guid Id { get; private set; }
    public string DeveloperEmail { get; private set; } = string.Empty;
    public string PlanName { get; private set; } = string.Empty;
    public decimal MonthlyPriceUsd { get; private set; }

    /// <summary>API-equivalent USD one 5-hour window allows; null when the lead has not set one.</summary>
    public decimal? WindowLimitUsd { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public static DeveloperPlan Create(
        string developerEmail, string planName, decimal monthlyPriceUsd, decimal? windowLimitUsd, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(developerEmail);
        var plan = new DeveloperPlan
        {
            Id = Guid.NewGuid(),
            DeveloperEmail = Common.DeveloperEmail.Normalize(developerEmail),
            CreatedAt = nowUtc,
        };
        plan.Update(planName, monthlyPriceUsd, windowLimitUsd, nowUtc);
        return plan;
    }

    public void Update(string planName, decimal monthlyPriceUsd, decimal? windowLimitUsd, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planName);
        PlanName = planName.Trim();
        MonthlyPriceUsd = monthlyPriceUsd;
        WindowLimitUsd = windowLimitUsd;
        UpdatedAt = nowUtc;
    }
}
