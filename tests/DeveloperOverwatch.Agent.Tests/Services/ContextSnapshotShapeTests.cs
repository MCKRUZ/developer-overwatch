using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using static DeveloperOverwatch.Agent.Tests.Services.ContextLines;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>The request shape gives the system prompt and the loaded tools; the snapshot is null without a current
/// shape or a measured main-thread response.</summary>
public sealed class ContextSnapshotShapeTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("overwatch-context-");

    public void Dispose() => _dir.Delete(recursive: true);

    private string Write(string name, params string[] lines)
    {
        var path = Path.Combine(_dir.FullName, name);
        File.WriteAllLines(path, lines);
        return path;
    }

    private Task<ContextSnapshot?> ReadAsync(params string[] lines) =>
        ContextSnapshotReader.ReadAsync(Write("s1.jsonl", lines), [], CancellationToken.None);

    private static string Big() => Assistant("msg_1", 10, 1_000_000);

    [Fact]
    public async Task TheCurrentShape_IsTheOneTheLastApiRequestNames()
    {
        var snapshot = await ReadAsync(
            Shape("h1", [new('a', 40)]),
            Shape("h2", [new('b', 400)]),
            ApiRequest("h2", "2026-01-01T00:00:01Z"),
            Big(),
            ApiRequest("h1", "2026-01-01T00:00:09Z", "2.1.41"));

        snapshot!.TakenAt.Should().Be(new DateTime(2026, 1, 1, 0, 0, 9, DateTimeKind.Utc));
        snapshot.TakenAt.Kind.Should().Be(DateTimeKind.Utc);
        snapshot.ClaudeCodeVersion.Should().Be("2.1.41");
        CategoryTokens(snapshot, "systemPrompt").Should().Be(Tokens(40));
    }

    [Fact]
    public async Task SystemPrompt_SumsEveryBlock()
    {
        var snapshot = await ReadAsync(Shape("h1", [new('a', 10), new('b', 31)]), ApiRequest("h1"), Big());

        CategoryTokens(snapshot!, "systemPrompt").Should().Be(Tokens(41));
    }

    [Fact]
    public async Task NoMatchingShape_IsNull()
    {
        (await ReadAsync(Shape("h1", ["sys"]), ApiRequest("h9"), Big())).Should().BeNull();
    }

    [Fact]
    public async Task NoApiRequest_IsNull()
    {
        (await ReadAsync(Shape("h1", ["sys"]), Big())).Should().BeNull();
    }

    [Fact]
    public async Task NoMainAssistantTurn_IsNull()
    {
        (await ReadAsync(Shape("h1", ["sys"]), ApiRequest("h1"), Assistant("msg_s", 10, 900, sidechain: true))).Should().BeNull();
    }

    [Fact]
    public async Task LoadedTool_IsNameDescriptionAndCompactSchema_DeferredAndPlaceholderAreSkipped()
    {
        // Compact: {"type":"object","properties":{"q":{"description":"a<b"}}} — 58 chars, '<' unescaped.
        const string schema = """{ "type": "object", "properties": { "q": { "description": "a<b" } } }""";
        var snapshot = await ReadAsync(
            Shape("h1", ["s"],
                Tool("Grep", new string('d', 30), schema),
                Tool("WebSearch", new string('x', 4000), deferLoading: true),
                Tool("DeferredToolPlaceholder", new string('p', 4000))),
            ApiRequest("h1"), Big());

        var grep = snapshot!.Tools.Should().ContainSingle().Subject;
        grep.Should().Be(new ContextToolGroup("Grep", false, 1, Tokens(4 + 30 + 58), false));
        CategoryTokens(snapshot, "builtInTools").Should().Be(Tokens(4 + 30 + 58));
    }

    [Fact]
    public async Task McpTools_GroupByServer_BuiltInsByTheirOwnName()
    {
        var snapshot = await ReadAsync(
            Shape("h1", ["s"],
                Tool("mcp__github__create_issue", new string('a', 100)),
                Tool("mcp__github__list_prs", new string('b', 100)),
                Tool("mcp__plugin_x_y__run", new string('c', 10)),
                Tool("Bash", new string('d', 50))),
            ApiRequest("h1"), Big());

        const int obj = 17; // {"type":"object"}
        long Chars(string name, int desc) => name.Length + desc + obj;
        var github = Tokens(Chars("mcp__github__create_issue", 100) + Chars("mcp__github__list_prs", 100));
        snapshot!.Tools.Should().Equal(
            new ContextToolGroup("github", true, 2, github, false),
            new ContextToolGroup("Bash", false, 1, Tokens(Chars("Bash", 50)), false),
            new ContextToolGroup("plugin_x_y", true, 1, Tokens(Chars("mcp__plugin_x_y__run", 10)), false));
        CategoryTokens(snapshot, "mcpTools", "github").Should().Be(github);
        CategoryTokens(snapshot, "mcpTools", "plugin_x_y").Should().Be(Tokens(Chars("mcp__plugin_x_y__run", 10)));
    }

    [Fact]
    public async Task Used_IsTheUnionOfToolsUsedAcrossTheSessionAndItsSubagents()
    {
        var main = Write("s1.jsonl",
            Shape("h1", ["s"], Tool("Bash"), Tool("Grep"), Tool("Read"), Tool("mcp__github__a"), Tool("mcp__jira__b")),
            ToolsUsed("Bash"), ApiRequest("h1"), Big());
        var sub = Write("agent-1.jsonl", ToolsUsed("Grep", "mcp__github__a"));

        var snapshot = await ContextSnapshotReader.ReadAsync(main, [sub], CancellationToken.None);

        snapshot!.Tools.ToDictionary(t => t.Group, t => t.Used).Should().BeEquivalentTo(new Dictionary<string, bool>
        {
            ["Bash"] = true, ["Grep"] = true, ["Read"] = false, ["github"] = true, ["jira"] = false
        });
    }

    [Fact]
    public async Task Measured_IsTheLatestMainResponseWithContext_CountedOnceAcrossItsLines()
    {
        var snapshot = await ReadAsync(
            Shape("h1", ["s"]), ApiRequest("h1"),
            Assistant("msg_1", 5, 100, 20, "claude-sonnet-4-5"),
            Assistant("msg_2", 3, 5000, 0, "claude-opus-5-5"),
            Assistant("msg_2", 3, 6000, 40, "claude-opus-5-5"),
            Assistant("msg_3", 0, 0, 0, "<synthetic>"),
            Assistant("msg_4", 1, 90_000, 0, "claude-haiku-4-5", sidechain: true));

        snapshot!.MeasuredContextTokens.Should().Be(3 + 6000 + 40);
        snapshot.Model.Should().Be("claude-opus-5-5");
    }

    [Fact]
    public async Task ApiRequestBlob_IsNeverRead()
    {
        string[] lines = [Shape("h1", ["s"], Tool("Bash")), Instructions("mem"), ApiRequest("h1"), Big()];
        var without = await ReadAsync(lines);

        var with = await ReadAsync([.. lines, Blob("BLOB-MARKER-7f3a")]);

        with.Should().BeEquivalentTo(without);
    }
}
