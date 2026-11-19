using System.Text.Json;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>Replays a main transcript's context lines in file order; its state after the last line is what the
/// snapshot is computed from. The listing attachments are deltas, and Claude Code sends every listing again after a
/// compaction, so each compaction starts them over. Sizes only are kept, never text.</summary>
internal sealed class ContextLedger
{
    private readonly Dictionary<string, RequestShape> _shapes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _agents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _mcpInstructions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _deferredTools = new(StringComparer.Ordinal);
    private readonly HashSet<string> _builtInAgents = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedTools = new(StringComparer.Ordinal);
    private readonly List<ContextCompaction> _compactions = [];
    private readonly LatestResponse _response = new();
    private string? _currentShapeHash;

    /// <summary>The time of the latest <c>api-request</c>.</summary>
    public DateTime? TakenAt { get; private set; }

    public string? ClaudeCodeVersion { get; private set; }

    /// <summary>The shape the latest <c>api-request</c> names, if it has been seen.</summary>
    public RequestShape? CurrentShape => _currentShapeHash is { } hash ? _shapes.GetValueOrDefault(hash) : null;

    public LatestResponse.Measure? MeasuredResponse => _response.Latest;
    public long MemoryChars { get; private set; }
    public long SkillChars { get; private set; }
    public long McpInstructionChars => _mcpInstructions.Values.Sum();
    public long CustomAgentChars => _agents.Where(a => !_builtInAgents.Contains(a.Key)).Sum(a => a.Value);
    public IReadOnlyDictionary<string, long> DeferredTools => _deferredTools;
    public IReadOnlySet<string> UsedTools => _usedTools;
    public IReadOnlyList<ContextCompaction> Compactions => _compactions;

    /// <summary>Adds one main-transcript line. A sidechain line belongs to a subagent, not this context.</summary>
    public void Add(JsonElement line)
    {
        if (TranscriptJson.IsTrue(line, "isSidechain"))
            return;

        switch (TranscriptJson.String(line, "type"))
        {
            case "api-request":
                _currentShapeHash = TranscriptJson.String(line, "shapeHash");
                TakenAt = TranscriptJson.Timestamp(line);
                ClaudeCodeVersion = TranscriptJson.String(line, "version");
                break;
            case "api-request-shape" when TranscriptJson.String(line, "shapeHash") is { } hash && RequestShape.From(line) is { } shape:
                _shapes[hash] = shape;
                break;
            case "tools-used":
                AddUsedTools(line);
                break;
            case "attachment" when TranscriptJson.TryObject(line, "attachment", out var attachment):
                AddAttachment(attachment);
                break;
            case "system" when TranscriptJson.String(line, "subtype") == "compact_boundary":
                AddCompaction(line);
                break;
            case "assistant":
                _response.Add(line);
                break;
        }
    }

    /// <summary>Names of tools actually called, from the main transcript or a subagent's.</summary>
    public void AddUsedTools(JsonElement line)
    {
        foreach (var name in TranscriptJson.Strings(line, "tools").OfType<string>())
            _usedTools.Add(name);
    }

    private void AddAttachment(JsonElement attachment)
    {
        switch (TranscriptJson.String(attachment, "type"))
        {
            case "instructions":
                MemoryChars = TranscriptJson.Objects(attachment, "files").Sum(f => (long)(TranscriptJson.String(f, "content")?.Length ?? 0));
                break;
            case "skill_listing":
                var chars = TranscriptJson.String(attachment, "content")?.Length ?? 0;
                SkillChars = TranscriptJson.IsTrue(attachment, "isInitial") ? chars : SkillChars + chars;
                break;
            case "agent_listing_delta":
                AddAgents(attachment);
                break;
            case "mcp_instructions_delta":
                ApplyDelta(_mcpInstructions, attachment, "addedNames", "addedBlocks", "removedNames");
                break;
            case "deferred_tools_delta":
                ApplyDelta(_deferredTools, attachment, "addedNames", "addedLines", "removedNames");
                break;
        }
    }

    /// <summary>The built-in set is the latest listing's, kept across resets: it classifies types rather than
    /// listing them.</summary>
    private void AddAgents(JsonElement delta)
    {
        if (TranscriptJson.IsTrue(delta, "isInitial"))
            _agents.Clear();

        if (TranscriptJson.HasArray(delta, "builtInTypes") || TranscriptJson.HasArray(delta, "builtInPluginTypes"))
        {
            _builtInAgents.Clear();
            _builtInAgents.UnionWith(TranscriptJson.Strings(delta, "builtInTypes").OfType<string>());
            _builtInAgents.UnionWith(TranscriptJson.Strings(delta, "builtInPluginTypes").OfType<string>());
        }

        ApplyDelta(_agents, delta, "addedTypes", "addedLines", "removedTypes");
    }

    /// <summary>Removals first, then additions, so a name removed and added again in one delta stays, at its new
    /// size. Each added name pairs with the text at the same index.</summary>
    private static void ApplyDelta(Dictionary<string, long> sizes, JsonElement delta, string namesKey, string textsKey, string removedKey)
    {
        foreach (var removed in TranscriptJson.Strings(delta, removedKey).OfType<string>())
            sizes.Remove(removed);

        var names = TranscriptJson.Strings(delta, namesKey);
        var texts = TranscriptJson.Strings(delta, textsKey);
        for (var i = 0; i < names.Count; i++)
        {
            if (names[i] is { } name)
                sizes[name] = i < texts.Count ? texts[i]?.Length ?? 0 : 0;
        }
    }

    /// <summary>A boundary without a time cannot be placed, so it is not listed; it still starts the listings over.</summary>
    private void AddCompaction(JsonElement line)
    {
        if (TranscriptJson.Timestamp(line) is { } at)
        {
            var metadata = TranscriptJson.TryObject(line, "compactMetadata", out var m) ? m : default;
            long Tokens(string name) =>
                metadata.ValueKind == JsonValueKind.Object ? Math.Max(0, TranscriptJson.Long(metadata, name) ?? 0) : 0;
            var trigger = metadata.ValueKind == JsonValueKind.Object ? TranscriptJson.String(metadata, "trigger") : null;
            _compactions.Add(new ContextCompaction(at, trigger ?? "unknown", Tokens("preTokens"), Tokens("postTokens")));
        }

        MemoryChars = 0;
        SkillChars = 0;
        _agents.Clear();
        _mcpInstructions.Clear();
        _deferredTools.Clear();
    }
}
