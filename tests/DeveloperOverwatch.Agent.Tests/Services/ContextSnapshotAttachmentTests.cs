using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using static DeveloperOverwatch.Agent.Tests.Services.ContextLines;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>The listing attachments are deltas replayed in file order, and a compaction makes Claude Code send
/// them again, so the snapshot is the state after the last line. The measured context is large here, so no
/// estimate is scaled.</summary>
public sealed class ContextSnapshotAttachmentTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("overwatch-context-");

    public void Dispose() => _dir.Delete(recursive: true);

    private async Task<ContextSnapshot> ReadAsync(params string[] lines)
    {
        var path = Path.Combine(_dir.FullName, "s1.jsonl");
        File.WriteAllLines(path, [Shape("h1", ["sys"]), .. lines, ApiRequest("h1"), Assistant("msg_1", 10, 1_000_000)]);
        var snapshot = await ContextSnapshotReader.ReadAsync(path, [], CancellationToken.None);
        return snapshot!;
    }

    [Fact]
    public async Task Instructions_TheLatestListingIsTheMemory()
    {
        var snapshot = await ReadAsync(Instructions(new string('a', 400)), Instructions(new string('b', 30), new string('c', 10)));

        CategoryTokens(snapshot, "memoryFiles").Should().Be(Tokens(40));
    }

    [Fact]
    public async Task Skills_AnInitialListingResetsTheTotal_OthersAddToIt()
    {
        var snapshot = await ReadAsync(
            Skills(new string('a', 400), isInitial: true),
            Skills(new string('b', 8), isInitial: false),
            Skills(new string('c', 20), isInitial: true),
            Skills(new string('d', 5), isInitial: false));

        CategoryTokens(snapshot, "skills").Should().Be(Tokens(25));
    }

    [Fact]
    public async Task Agents_CountOnlyTypesOutsideTheLatestBuiltIns_AfterRemovals()
    {
        var snapshot = await ReadAsync(
            Agents(["Explore", "reviewer", "planner", "plugin:helper"], [new('e', 100), new('r', 40), new('p', 24), new('h', 50)],
                [], isInitial: true, builtIn: ["Explore"], builtInPlugin: ["plugin:helper"]),
            Agents(["writer"], [new('w', 12)], ["planner"], isInitial: false, builtIn: ["Explore"], builtInPlugin: ["plugin:helper"]));

        CategoryTokens(snapshot, "customAgents").Should().Be(Tokens(40 + 12));
    }

    [Fact]
    public async Task Agents_AnInitialListingDropsEarlierTypes()
    {
        var snapshot = await ReadAsync(
            Agents(["reviewer"], [new('r', 400)], [], isInitial: true),
            Agents(["writer"], [new('w', 16)], [], isInitial: true));

        CategoryTokens(snapshot, "customAgents").Should().Be(Tokens(16));
    }

    [Fact]
    public async Task Agents_ARemovedAndReAddedTypeInOneDeltaStays()
    {
        var snapshot = await ReadAsync(
            Agents(["reviewer"], [new('r', 40)], [], isInitial: true),
            Agents(["reviewer"], [new('r', 20)], ["reviewer"], isInitial: false));

        CategoryTokens(snapshot, "customAgents").Should().Be(Tokens(20));
    }

    [Fact]
    public async Task McpInstructions_MapEachServerToItsBlock_RemovalsDelete()
    {
        var snapshot = await ReadAsync(
            McpInstructions(["github", "jira"], [new('g', 80), new('j', 400)]),
            McpInstructions(["github"], [new('g', 36)], "jira"));

        CategoryTokens(snapshot, "mcpInstructions").Should().Be(Tokens(36));
    }

    [Fact]
    public async Task DeferredNames_CountTheirLines_InBuiltInOrTheirMcpServer()
    {
        var snapshot = await ReadAsync(
            Deferred(["WebFetch", "mcp__github__create_issue", "NotebookEdit"], [new('w', 20), new('g', 44), new('n', 400)]),
            Deferred([], [], "NotebookEdit"));

        CategoryTokens(snapshot, "builtInTools").Should().Be(Tokens(20));
        CategoryTokens(snapshot, "mcpTools", "github").Should().Be(Tokens(44));
        snapshot.Tools.Select(t => (t.Group, t.IsMcp, t.ToolCount)).Should().BeEquivalentTo(
            [("github", true, 1), ("WebFetch", false, 1)]);
    }

    [Fact]
    public async Task CompactBoundary_ResetsEveryListing_AndIsRecorded()
    {
        var snapshot = await ReadAsync(
            Instructions(new string('a', 400)),
            Skills(new string('s', 400), isInitial: true),
            Agents(["reviewer"], [new('r', 400)], [], isInitial: true),
            McpInstructions(["github"], [new('g', 400)]),
            Deferred(["WebFetch"], [new('w', 400)]),
            Compact("2026-01-01T00:00:03Z", "auto", 180_000, 20_000),
            Skills(new string('t', 12), isInitial: false));

        CategoryTokens(snapshot, "memoryFiles").Should().Be(0);
        CategoryTokens(snapshot, "customAgents").Should().Be(0);
        CategoryTokens(snapshot, "mcpInstructions").Should().Be(0);
        CategoryTokens(snapshot, "builtInTools").Should().Be(0);
        CategoryTokens(snapshot, "skills").Should().Be(Tokens(12));
        snapshot.Compactions.Should().Equal(
            new ContextCompaction(new DateTime(2026, 1, 1, 0, 0, 3, DateTimeKind.Utc), "auto", 180_000, 20_000));
    }

    [Fact]
    public async Task SidechainAttachments_AreNotTheMainContext()
    {
        var snapshot = await ReadAsync(
            Attachment(new { type = "skill_listing", content = new string('s', 400), isInitial = true }, sidechain: true));

        CategoryTokens(snapshot, "skills").Should().Be(0);
    }

    [Fact]
    public async Task CategoriesWithNoTokens_AreLeftOut_ButConversationIsAlwaysThere()
    {
        var snapshot = await ReadAsync();

        snapshot.Categories.Select(c => c.Category).Should().Equal("systemPrompt", "conversation");
    }
}
