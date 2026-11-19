using System.Text.Json;
using System.Text.Json.Nodes;
using DeveloperOverwatch.Agent.Services;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>Hand-built transcript lines in the Claude Code 2.1.x structure the context snapshot reads. None is
/// copied from a real transcript.</summary>
internal static class ContextLines
{
    public const string Ts = "2026-01-01T00:00:05Z";

    public static string ApiRequest(string hash, string ts = Ts, string version = "2.1.40") =>
        Line(new
        {
            type = "api-request", sessionId = "s1", id = "req_x", timestamp = ts, version,
            querySource = "repl_main_thread", shapeHash = hash, @params = new { model = "claude-opus-5-5" }
        });

    public static string Shape(string hash, string[] system, params object[] tools) =>
        Line(new
        {
            type = "api-request-shape", sessionId = "s1", shapeHash = hash, timestamp = Ts, version = "2.1.40",
            shape = new { system = system.Select(text => new { type = "text", text }), tools }
        });

    public static object Tool(string name, string description = "", string schema = """{"type":"object"}""", bool deferLoading = false) =>
        deferLoading
            ? new { name, description, input_schema = JsonNode.Parse(schema), defer_loading = true }
            : new { name, description, input_schema = JsonNode.Parse(schema) };

    public static string Instructions(params string[] contents) =>
        Attachment(new { type = "instructions", files = contents.Select(content => new { path = "C:/x/CLAUDE.md", type = "Project", content }) });

    public static string Skills(string content, bool isInitial) =>
        Attachment(new { type = "skill_listing", content, skillCount = 1, isInitial, names = new[] { "s" }, digests = new[] { "d" } });

    public static string Agents(
        string[] added, string[] lines, string[] removed, bool isInitial, string[]? builtIn = null, string[]? builtInPlugin = null) =>
        Attachment(new
        {
            type = "agent_listing_delta", addedTypes = added, addedLines = lines,
            builtInTypes = builtIn ?? [], builtInPluginTypes = builtInPlugin ?? [], removedTypes = removed, isInitial
        });

    public static string McpInstructions(string[] added, string[] blocks, params string[] removed) =>
        Attachment(new { type = "mcp_instructions_delta", addedNames = added, addedBlocks = blocks, removedNames = removed });

    public static string Deferred(string[] added, string[] lines, params string[] removed) =>
        Attachment(new { type = "deferred_tools_delta", addedNames = added, addedLines = lines, removedNames = removed, readdedNames = Array.Empty<string>() });

    public static string Attachment(object attachment, bool sidechain = false) =>
        Line(new { type = "attachment", isSidechain = sidechain, timestamp = Ts, attachment });

    public static string Compact(string ts, string trigger, long preTokens, long postTokens) =>
        Line(new
        {
            type = "system", subtype = "compact_boundary", timestamp = ts,
            compactMetadata = new { trigger, preTokens, postTokens }
        });

    public static string ToolsUsed(params string[] tools) => Line(new { type = "tools-used", sessionId = "s1", tools });

    public static string Assistant(
        string messageId, int input, int cacheRead, int cacheWrite = 0, string model = "claude-opus-5-5",
        string ts = Ts, bool sidechain = false) =>
        Line(new
        {
            type = "assistant", isSidechain = sidechain, uuid = Guid.NewGuid().ToString(), timestamp = ts,
            message = new
            {
                id = messageId, role = "assistant", model, content = new[] { new { type = "text", text = "ok" } },
                usage = new { input_tokens = input, output_tokens = 7, cache_creation_input_tokens = cacheWrite, cache_read_input_tokens = cacheRead }
            }
        });

    /// <summary>A request copy whose body would move every total if it were read: it nests a shape-like system and
    /// tool list, an attachment-like listing, a tools-used list and an assistant response.</summary>
    public static string Blob(string marker) =>
        Line(new
        {
            type = "api-request-blob", sessionId = "s1", shapeHash = "h1", timestamp = Ts,
            @params = new
            {
                system = new[] { new { type = "text", text = marker + new string('x', 5000) } },
                tools = new[] { new { name = "mcp__blob__leak", description = marker, input_schema = new { type = "object" } } },
                messages = new object[]
                {
                    new { type = "attachment", attachment = new { type = "skill_listing", content = marker, isInitial = true } },
                    new { type = "tools-used", tools = new[] { "Bash", "mcp__blob__leak" } },
                    new { type = "assistant", message = new { id = "msg_blob", usage = new { input_tokens = 999999 } } }
                }
            }
        });

    public static string Line(object value) => JsonSerializer.Serialize(value);

    public static int Tokens(long chars) => (int)((chars + 3) / 4);

    public static long CategoryTokens(ContextSnapshot snapshot, string category, string? server = null) =>
        snapshot.Categories.Where(c => c.Category == category && c.Server == server).Sum(c => c.Tokens);
}
