using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using static DeveloperOverwatch.Agent.Tests.Services.ContextLines;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>The estimates are character counts; the measured context is what the API reported. Conversation is
/// what the estimates leave over, and estimates larger than the measurement are scaled down to fit it. The lists
/// stay inside the server's bounds, since a snapshot it refuses would take the turns of its POST with it.</summary>
public sealed class ContextSnapshotScalingTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("overwatch-context-");

    public void Dispose() => _dir.Delete(recursive: true);

    private async Task<ContextSnapshot> ReadAsync(params string[] lines)
    {
        var path = Path.Combine(_dir.FullName, "s1.jsonl");
        File.WriteAllLines(path, lines);
        return (await ContextSnapshotReader.ReadAsync(path, [], CancellationToken.None))!;
    }

    [Fact]
    public async Task EstimatesBelowTheMeasurement_LeaveTheRestToConversation()
    {
        var snapshot = await ReadAsync(
            Shape("h1", [new('s', 400)], Tool("Bash", new string('d', 79))), // 4 + 79 + 17 = 100 chars
            Skills(new string('k', 200), isInitial: true),
            ApiRequest("h1"),
            Assistant("msg_1", 100, 900, 0));

        snapshot.MeasuredContextTokens.Should().Be(1000);
        snapshot.Categories.Select(c => (c.Category, c.Tokens)).Should().Equal(
            ("systemPrompt", 100L), ("builtInTools", 25L), ("skills", 50L), ("conversation", 825L));
        snapshot.Tools.Single().TokensPerRequest.Should().Be(25);
    }

    [Fact]
    public async Task EstimatesAboveTheMeasurement_AreScaledToIt_AndConversationIsZero()
    {
        var snapshot = await ReadAsync(
            Shape("h1", [new('s', 1200)], Tool("mcp__github__a", new string('d', 369))), // 14 + 369 + 17 = 400 chars
            McpInstructions(["github"], [new('g', 400)]),
            ApiRequest("h1"),
            Assistant("msg_1", 10, 240, 0));

        // fixed = 300 + 100 + 100 = 500 tokens against 250 measured: factor 0.5.
        snapshot.Categories.Select(c => (c.Category, c.Server, c.Tokens)).Should().Equal(
            ("systemPrompt", null, 150L), ("mcpTools", "github", 50L), ("mcpInstructions", null, 50L), ("conversation", null, 0L));
        snapshot.Tools.Single().TokensPerRequest.Should().Be(50);
    }

    [Fact]
    public async Task ScaledTokens_AreRounded()
    {
        var snapshot = await ReadAsync(
            Shape("h1", [new('s', 40)]),
            Skills(new string('k', 20), isInitial: true),
            ApiRequest("h1"),
            Assistant("msg_1", 1, 9, 0));

        // fixed = 10 + 5 = 15 against 10: 6.67 and 3.33.
        snapshot.Categories.Select(c => c.Tokens).Should().Equal(7L, 3L, 0L);
    }

    [Fact]
    public async Task MoreThanThirteenMcpServers_FoldTheSmallestIntoOneRow_KeepingTheTotal()
    {
        // Seven other categories, conversation included, leave 13 of the server's 20 rows for MCP servers.
        var tools = Enumerable.Range(1, 15)
            .Select(i => Tool($"mcp__srv{i:D2}__t", new string('d', 100 * i)))
            .ToArray();
        var snapshot = await ReadAsync(
            Shape("h1", ["s"], [.. tools, Tool("Bash", "b")]),
            McpInstructions(["srv01"], ["i"]), Agents(["a"], ["a"], [], isInitial: true), Instructions("m"), Skills("k", isInitial: true),
            ApiRequest("h1"), Assistant("msg_1", 1, 1_000_000));

        var mcp = snapshot.Categories.Where(c => c.Category == "mcpTools").ToList();
        mcp.Should().HaveCount(13);
        mcp.Take(12).Select(c => c.Server).Should().Equal(Enumerable.Range(4, 12).Reverse().Select(i => $"srv{i:D2}"));
        mcp[12].Server.Should().Be(ContextSnapshotCalculator.OtherServers);
        mcp[12].Tokens.Should().Be(snapshot.Tools.Where(t => t.Group is "srv01" or "srv02" or "srv03").Sum(t => t.TokensPerRequest));
        snapshot.Categories.Should().HaveCount(20);
        snapshot.Tools.Should().HaveCount(16, "the tool list keeps every server");
    }

    [Fact]
    public async Task ToolGroups_AreCappedAt200_LargestFirst()
    {
        var tools = Enumerable.Range(1, 210).Select(i => Tool($"T{i:D3}", new string('d', i * 4))).ToArray();
        var snapshot = await ReadAsync(Shape("h1", ["s"], tools), ApiRequest("h1"), Assistant("msg_1", 1, 1_000_000));

        snapshot.Tools.Should().HaveCount(200);
        snapshot.Tools[0].Group.Should().Be("T210");
        snapshot.Tools[^1].Group.Should().Be("T011");
    }

    [Fact]
    public async Task Compactions_AreCappedAtTheLatest50_InOrder()
    {
        var compactions = Enumerable.Range(0, 55)
            .Select(i => Compact(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i).ToString("o"), "manual", i, 1));
        var snapshot = await ReadAsync([.. compactions, Shape("h1", ["s"]), ApiRequest("h1"), Assistant("msg_1", 1, 1_000_000)]);

        snapshot.Compactions.Should().HaveCount(50);
        snapshot.Compactions[0].PreTokens.Should().Be(5);
        snapshot.Compactions[^1].PreTokens.Should().Be(54);
    }

    [Fact]
    public async Task LongNames_AreClampedToTheServerBounds()
    {
        var server = new string('s', 300);
        var snapshot = await ReadAsync(
            Shape("h1", ["s"], Tool($"mcp__{server}__t"), Tool(new string('B', 300))),
            Compact(Ts, new string('t', 30), 1, 1),
            ApiRequest("h1", version: new string('v', 80)),
            Assistant("msg_1", 1, 1_000_000, model: new string('m', 300)));

        snapshot.ClaudeCodeVersion.Should().HaveLength(50);
        snapshot.Model.Should().HaveLength(FieldLimits.Name);
        snapshot.Compactions.Single().Trigger.Should().HaveLength(20);
        snapshot.Tools.Should().OnlyContain(t => t.Group.Length == FieldLimits.Name);
        snapshot.Categories.Single(c => c.Category == "mcpTools").Server.Should().HaveLength(FieldLimits.Name);
    }
}
