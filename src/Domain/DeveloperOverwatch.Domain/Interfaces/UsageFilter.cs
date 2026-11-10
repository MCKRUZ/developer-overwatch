namespace DeveloperOverwatch.Domain.Interfaces;

/// <summary>Narrows usage to one developer and/or one project; a null field does not filter.</summary>
/// <param name="DeveloperEmail">Exact match on the session's developer email.</param>
/// <param name="Project">Exact match on the session's project key, or its repository path when it has no key.</param>
public sealed record UsageFilter(string? DeveloperEmail, string? Project)
{
    public static readonly UsageFilter None = new(null, null);
}
