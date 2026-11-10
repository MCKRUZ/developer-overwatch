namespace DeveloperOverwatch.Agent.Services;

/// <summary>Instants compared across hooks, the queue and transcripts, which disagree on DateTimeKind.</summary>
internal static class UtcInstant
{
    /// <summary>An unspecified kind is taken as UTC already, as the hooks write it; converting it with
    /// ToUniversalTime would shift it by the machine's offset.</summary>
    public static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value
    };

    /// <summary>The later of the two, in UTC; <paramref name="b"/> when <paramref name="a"/> is null.</summary>
    public static DateTime Later(DateTime? a, DateTime b)
    {
        var right = Utc(b);
        return a is { } left && Utc(left) > right ? Utc(left) : right;
    }
}
