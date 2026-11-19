namespace DeveloperOverwatch.Domain.Entities;

/// <summary>One compaction of the session's context, with its size before and after.</summary>
public sealed class SessionContextCompaction
{
    public SessionContextCompaction(DateTime at, string trigger, long preTokens, long postTokens)
    {
        At = at;
        Trigger = trigger;
        PreTokens = preTokens;
        PostTokens = postTokens;
    }

    public DateTime At { get; private set; }

    /// <summary>"auto", "manual", or "unknown" when the transcript did not say.</summary>
    public string Trigger { get; private set; }

    public long PreTokens { get; private set; }
    public long PostTokens { get; private set; }
}
