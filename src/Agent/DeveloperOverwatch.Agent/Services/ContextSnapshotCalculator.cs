namespace DeveloperOverwatch.Agent.Services;

/// <summary>Turns a replayed <see cref="ContextLedger"/> into a <see cref="ContextSnapshot"/>. Each category is
/// estimated at four characters a token. The measured context is the truth: conversation is what the estimates
/// leave of it, and estimates that exceed it are scaled down to fit, tool groups by the same factor.</summary>
internal static class ContextSnapshotCalculator
{
    /// <summary>The server takes at most this many categories, tool groups and compactions; a snapshot over a bound
    /// is refused, and the turns of its POST with it.</summary>
    public const int MaxCategories = 20;
    public const int MaxToolGroups = 200;
    public const int MaxCompactions = 50;
    public const int MaxVersion = 50;
    public const int MaxTrigger = 20;

    /// <summary>The row the smallest MCP servers are folded into when there are more than the category bound allows.</summary>
    public const string OtherServers = "(other servers)";

    private const string McpPrefix = "mcp__";

    // Every category but MCP tools has one row, and conversation is always sent.
    private const int MaxMcpServerRows = MaxCategories - 7;

    private sealed record ToolGroup(string Group, bool IsMcp, int ToolCount, long Tokens, bool Used);

    /// <summary>Null without a current shape, a request time or a measured main-thread response.</summary>
    public static ContextSnapshot? Compute(ContextLedger ledger)
    {
        if (ledger.CurrentShape is not { } shape || ledger.TakenAt is not { } takenAt || ledger.MeasuredResponse is not { } measured)
            return null;

        var groups = ToolGroups(shape, ledger);
        var estimates = Estimates(shape, ledger, groups);
        var measuredTokens = measured.ContextTokens;
        var fixedTokens = estimates.Sum(e => e.Tokens);
        var factor = fixedTokens > measuredTokens ? (double)measuredTokens / fixedTokens : 1d;
        long Scale(long tokens) => factor == 1d ? tokens : (long)Math.Round(tokens * factor, MidpointRounding.AwayFromZero);

        var categories = estimates
            .Select(e => e with { Tokens = Scale(e.Tokens), Server = FieldLimits.Clamp(e.Server, FieldLimits.Name) })
            .Where(e => e.Tokens > 0)
            .Append(new ContextCategoryTokens(ContextCategories.Conversation, null, Math.Max(0, measuredTokens - fixedTokens)))
            .ToList();
        var tools = groups
            .OrderByDescending(g => g.Tokens).ThenBy(g => g.Group, StringComparer.Ordinal)
            .Take(MaxToolGroups)
            .Select(g => new ContextToolGroup(FieldLimits.Clamp(g.Group, FieldLimits.Name), g.IsMcp, g.ToolCount, Scale(g.Tokens), g.Used))
            .ToList();
        var compactions = ledger.Compactions
            .TakeLast(MaxCompactions)
            .Select(c => c with { Trigger = FieldLimits.Clamp(c.Trigger, MaxTrigger) })
            .ToList();

        return new ContextSnapshot(
            takenAt,
            FieldLimits.Clamp(ledger.ClaudeCodeVersion ?? "unknown", MaxVersion),
            FieldLimits.Clamp(measured.Model ?? "unknown", FieldLimits.Name),
            measuredTokens, categories, tools, compactions);
    }

    /// <summary>The fixed categories in display order, before scaling. A zero row is dropped after scaling.</summary>
    private static List<ContextCategoryTokens> Estimates(RequestShape shape, ContextLedger ledger, IReadOnlyList<ToolGroup> groups) =>
    [
        new(ContextCategories.SystemPrompt, null, Tokens(shape.SystemChars)),
        new(ContextCategories.BuiltInTools, null, groups.Where(g => !g.IsMcp).Sum(g => g.Tokens)),
        .. McpServerRows(groups),
        new(ContextCategories.McpInstructions, null, Tokens(ledger.McpInstructionChars)),
        new(ContextCategories.CustomAgents, null, Tokens(ledger.CustomAgentChars)),
        new(ContextCategories.MemoryFiles, null, Tokens(ledger.MemoryChars)),
        new(ContextCategories.Skills, null, Tokens(ledger.SkillChars))
    ];

    /// <summary>One row per server, largest first. Past the bound, the smallest are folded into one
    /// <see cref="OtherServers"/> row, so the total is unchanged.</summary>
    private static IEnumerable<ContextCategoryTokens> McpServerRows(IReadOnlyList<ToolGroup> groups)
    {
        var servers = groups.Where(g => g.IsMcp)
            .OrderByDescending(g => g.Tokens).ThenBy(g => g.Group, StringComparer.Ordinal)
            .Select(g => new ContextCategoryTokens(ContextCategories.McpTools, g.Group, g.Tokens))
            .ToList();
        if (servers.Count <= MaxMcpServerRows)
            return servers;

        var kept = servers.Take(MaxMcpServerRows - 1);
        var folded = servers.Skip(MaxMcpServerRows - 1).Sum(s => s.Tokens);
        return kept.Append(new ContextCategoryTokens(ContextCategories.McpTools, OtherServers, folded));
    }

    /// <summary>Loaded definitions and deferred name lines, grouped: an MCP tool by its server, a built-in tool by
    /// its own name. A group is used when any of its tools was called.</summary>
    private static List<ToolGroup> ToolGroups(RequestShape shape, ContextLedger ledger) =>
        shape.Tools.Select(t => (t.Name, t.Chars))
            .Concat(ledger.DeferredTools.Select(d => (Name: d.Key, Chars: d.Value)))
            .GroupBy(t => GroupOf(t.Name))
            .Select(g => new ToolGroup(
                g.Key.Group,
                g.Key.IsMcp,
                g.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count(),
                Tokens(g.Sum(t => t.Chars)),
                g.Any(t => ledger.UsedTools.Contains(t.Name))))
            .ToList();

    /// <summary><c>mcp__&lt;server&gt;__&lt;tool&gt;</c> belongs to <c>&lt;server&gt;</c>, the text up to the next
    /// <c>__</c>.</summary>
    private static (string Group, bool IsMcp) GroupOf(string name)
    {
        if (!name.StartsWith(McpPrefix, StringComparison.Ordinal) || name.Length == McpPrefix.Length)
            return (name, false);

        var rest = name[McpPrefix.Length..];
        var end = rest.IndexOf("__", StringComparison.Ordinal);
        return (end > 0 ? rest[..end] : rest, true);
    }

    private static long Tokens(long chars) => (chars + 3) / 4;
}
