namespace DeveloperOverwatch.Domain.Entities;

/// <summary>A group of tool definitions sent on every request (built-in, or one MCP server), what they cost
/// per request, and whether the session called any of them.</summary>
public sealed class SessionContextToolGroup
{
    public SessionContextToolGroup(string group, bool isMcp, int toolCount, long tokensPerRequest, bool used)
    {
        Group = group;
        IsMcp = isMcp;
        ToolCount = toolCount;
        TokensPerRequest = tokensPerRequest;
        Used = used;
    }

    public string Group { get; private set; }
    public bool IsMcp { get; private set; }
    public int ToolCount { get; private set; }
    public long TokensPerRequest { get; private set; }
    public bool Used { get; private set; }
}
