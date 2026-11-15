namespace DeveloperOverwatch.Domain.Common;

/// <summary>What started a user turn, as the Agent classifies it. Assistant and subagent turns, and turns
/// from Agents older than the classification, carry no kind.</summary>
public static class TurnKinds
{
    /// <summary>A prompt the developer typed.</summary>
    public const string Prompt = "prompt";

    /// <summary>Text Claude Code put in the user's place: skill bodies, hook output, subagent hand-backs.</summary>
    public const string Injected = "injected";

    /// <summary>The summary that replaces the conversation after a compaction.</summary>
    public const string CompactSummary = "compactSummary";

    /// <summary>The longest kind, for the stored column.</summary>
    public const int MaxLength = 20;

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(StringComparer.Ordinal) { Prompt, Injected, CompactSummary };
}
