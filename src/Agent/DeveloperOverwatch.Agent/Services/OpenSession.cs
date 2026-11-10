namespace DeveloperOverwatch.Agent.Services;

/// <summary>A session that may ship, and the instant its content may ship from. A floor of
/// <see cref="DateTime.MinValue"/> means everything of the session may ship.</summary>
internal sealed record OpenSession
{
    internal OpenSession(string sessionId, DateTime floor)
    {
        SessionId = sessionId;
        Floor = UtcInstant.Utc(floor);
    }

    public string SessionId { get; }

    /// <summary>Always UTC.</summary>
    public DateTime Floor { get; }

    public bool Bounded => Floor > DateTime.MinValue;

    /// <summary>Whether content dated <paramref name="at"/> may ship. Undated content passes only an
    /// unbounded session, since nothing shows it is from after the floor.</summary>
    public bool Admits(DateTime? at) => !Bounded || (at is { } t && UtcInstant.Utc(t) >= Floor);
}
