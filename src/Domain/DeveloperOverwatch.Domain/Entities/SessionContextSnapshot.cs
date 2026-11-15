namespace DeveloperOverwatch.Domain.Entities;

/// <summary>
/// What a session's context window held at one moment, broken down by category, with the tool groups sent
/// on every request and the compactions so far. A session keeps only its latest snapshot: each one the
/// Agent sends is complete, so a newer one replaces the stored one whole.
/// </summary>
public sealed class SessionContextSnapshot
{
    private readonly List<SessionContextCategory> _categories = [];
    private readonly List<SessionContextToolGroup> _tools = [];
    private readonly List<SessionContextCompaction> _compactions = [];

    private SessionContextSnapshot() { }

    public string SessionId { get; private set; } = string.Empty;

    /// <summary>When the Agent measured it, in UTC.</summary>
    public DateTime TakenAt { get; private set; }

    public string ClaudeCodeVersion { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;

    /// <summary>The context size of the latest main-thread response, which the categories add up to.</summary>
    public long MeasuredContextTokens { get; private set; }

    public IReadOnlyList<SessionContextCategory> Categories => _categories;
    public IReadOnlyList<SessionContextToolGroup> Tools => _tools;

    /// <summary>The latest compactions the Agent reports, oldest first.</summary>
    public IReadOnlyList<SessionContextCompaction> Compactions => _compactions;

    public static SessionContextSnapshot Create(
        string sessionId,
        DateTime takenAt,
        string claudeCodeVersion,
        string model,
        long measuredContextTokens,
        IEnumerable<SessionContextCategory> categories,
        IEnumerable<SessionContextToolGroup> tools,
        IEnumerable<SessionContextCompaction> compactions)
    {
        var snapshot = new SessionContextSnapshot
        {
            SessionId = sessionId,
            TakenAt = takenAt,
            ClaudeCodeVersion = claudeCodeVersion,
            Model = model,
            MeasuredContextTokens = measuredContextTokens,
        };
        snapshot._categories.AddRange(categories);
        snapshot._tools.AddRange(tools);
        snapshot._compactions.AddRange(compactions);
        return snapshot;
    }

    /// <summary>Takes every value of <paramref name="incoming"/> unless it was taken before this one.
    /// Snapshots can arrive out of order (retries), and an equal time is a resend of the same state.</summary>
    /// <returns>Whether this snapshot was replaced.</returns>
    public bool ReplaceUnlessOlder(SessionContextSnapshot incoming)
    {
        if (incoming.TakenAt < TakenAt)
            return false;

        TakenAt = incoming.TakenAt;
        ClaudeCodeVersion = incoming.ClaudeCodeVersion;
        Model = incoming.Model;
        MeasuredContextTokens = incoming.MeasuredContextTokens;
        Replace(_categories, incoming._categories);
        Replace(_tools, incoming._tools);
        Replace(_compactions, incoming._compactions);
        return true;
    }

    private static void Replace<T>(List<T> target, IEnumerable<T> source)
    {
        target.Clear();
        target.AddRange(source);
    }
}
