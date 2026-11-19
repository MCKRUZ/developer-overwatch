using System.Text.Json;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>Streams a whole session transcript, and the subagent transcripts beside it, into a
/// <see cref="ContextSnapshot"/>. Most lines are conversation, so a line is parsed only when its text holds one of
/// the line types the snapshot reads. An <c>api-request-blob</c> line holds the developer's own messages and is
/// never parsed or sized.</summary>
internal static class ContextSnapshotReader
{
    private const string BlobToken = "\"type\":\"api-request-blob\"";
    private const string ToolsUsedToken = "\"type\":\"tools-used\"";

    // "api-request" also matches "api-request-shape"; the blob is ruled out before this list is consulted.
    private static readonly string[] MainTokens =
    [
        "\"type\":\"api-request", ToolsUsedToken, "\"type\":\"attachment\"", "\"subtype\":\"compact_boundary\"", "\"type\":\"assistant\""
    ];

    private static readonly string[] SubagentTokens = [ToolsUsedToken];

    /// <summary>Null when the session has no current request shape or no measured response yet. A subagent
    /// transcript adds only the tools it called.</summary>
    /// <exception cref="IOException">A transcript could not be read.</exception>
    public static async Task<ContextSnapshot?> ReadAsync(
        string transcriptPath, IReadOnlyList<string> subagentPaths, CancellationToken ct)
    {
        if (!File.Exists(transcriptPath))
            return null;

        var ledger = new ContextLedger();
        await ReplayAsync(transcriptPath, MainTokens, ledger.Add, ct);
        foreach (var path in subagentPaths)
            await ReplayAsync(path, SubagentTokens, ledger.AddUsedTools, ct);

        return ContextSnapshotCalculator.Compute(ledger);
    }

    private static async Task ReplayAsync(string path, string[] tokens, Action<JsonElement> add, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!IsCandidate(line, tokens) || TryParse(line) is not { } doc)
                continue;

            using (doc)
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    add(doc.RootElement);
            }
        }
    }

    private static bool IsCandidate(string line, string[] tokens) =>
        !line.Contains(BlobToken, StringComparison.Ordinal)
        && tokens.Any(token => line.Contains(token, StringComparison.Ordinal));

    /// <summary>A line that is not JSON — most likely the one Claude Code is still writing — is skipped.</summary>
    private static JsonDocument? TryParse(string line)
    {
        try
        {
            return JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
