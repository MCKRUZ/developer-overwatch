namespace DeveloperOverwatch.API.Tests.Delivery;

using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;
using Xunit.Abstractions;

/// <summary>The delivery contract's invariants against the real server: the real agent queue, ApiClient and
/// processor send to the real API and its migrated database, in memory, under seeded faults that include
/// the agent dying after the server committed. Unlike the fake-server test in the agent's suite, the
/// server's validation, idempotency, foreign keys and aggregates are the production code. The class runs
/// alone, so its wall-clock bound measures the run, not other tests.</summary>
[Collection(nameof(RealServerDeliveryCollection))]
public sealed class RealServerDeliveryTests(ITestOutputHelper output)
{
    private static readonly int[] SeedList = [20261109, 7, 42, 1337];

    /// <summary>Each seed runs once per test process; the union fact reuses the theory's runs.</summary>
    private static readonly ConcurrentDictionary<int, Lazy<Task<RealServerResult>>> Runs = new();

    public static TheoryData<int> Seeds() => new(SeedList);

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task UnderSeededFaults_TheRealServerStoresEveryItemOnce_AndTheQueueDrains(int seed)
    {
        var run = await RunAsync(seed);
        output.WriteLine(run.Summary());
        var stored = run.Stored;
        var workload = run.Workload;

        using (new AssertionScope())
        {
            run.PendingAtExit.Should().BeEmpty("once the faults stop, every item reaches a definitive outcome");
            run.Handler.Unexpected.Should().BeEmpty("the real server answers every request the agent sends with 200");
            run.Handler.ServerStatuses.Should().NotContainKey(HttpStatusCode.TooManyRequests);

            var turnIds = stored.Turns.Select(t => t.Id).ToList();
            turnIds.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(workload.Turns);
            stored.ToolIds.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(workload.Tools);
            stored.FileIds.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(workload.Files);
            stored.FailureIds.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(workload.Failures);

            turnIds.Should().NotContain(RealServerWorkload.PoisonId);
            run.Handler.ReportedRejected.Should().Contain(RealServerWorkload.PoisonId, "the server rejects the role-less turn per item");

            stored.Sessions.ToDictionary(s => s.Id, s => s.Status).Should().BeEquivalentTo(RealServerWorkload.ExpectedStatuses);
            stored.Turns.Should().NotContain(t => t.Content != null && t.Content.Contains("secret " + RealServerWorkload.D));
            stored.ToolIds.Concat(stored.FailureIds).Should().NotContain(id => id.Contains(RealServerWorkload.D));

            AssertAggregates(run);
            run.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
        }
    }

    [Fact]
    public async Task AcrossTheSeeds_EveryFaultFires_TheAgentRestarts_AndATurnChunkIsFull()
    {
        var runs = new List<RealServerResult>();
        foreach (var seed in SeedList)
            runs.Add(await RunAsync(seed));

        using (new AssertionScope())
        {
            runs.SelectMany(r => r.Handler.Fired.Keys).Distinct()
                .Should().BeEquivalentTo(Enum.GetValues<RealServerFault>(), "the runs prove something under every fault only if each one fired");
            runs.Sum(r => r.Restarts).Should().BePositive("the agent must die after a commit at least once");
            runs.SelectMany(r => r.Handler.EventsTurnCounts).Should().Contain(200, "a big session ships in chunks of 200 turns");
        }
    }

    /// <summary>Redelivery must not double-count: tokens and failure counts move only for new items, and
    /// turn indexes stay contiguous however the turns arrived.</summary>
    private static void AssertAggregates(RealServerResult run)
    {
        foreach (var session in run.Stored.Sessions)
        {
            var turns = run.Workload.TurnsPerSession.GetValueOrDefault(session.Id);
            session.TotalInputTokens.Should().Be(10L * turns, $"{session.Id} input tokens");
            session.TotalOutputTokens.Should().Be(5L * turns, $"{session.Id} output tokens");
            session.ToolErrorCount.Should().Be(run.Workload.FailuresPerSession.GetValueOrDefault(session.Id), $"{session.Id} tool errors");
            run.Stored.Turns.Where(t => t.SessionId == session.Id).Select(t => t.TurnIndex).Order()
                .Should().Equal(Enumerable.Range(0, turns), $"{session.Id} turn indexes");
        }
    }

    private static Task<RealServerResult> RunAsync(int seed) =>
        Runs.GetOrAdd(seed, s => new Lazy<Task<RealServerResult>>(() => RealServerRun.RunAsync(s))).Value;
}

/// <summary>Runs <see cref="RealServerDeliveryTests"/> with nothing beside it.</summary>
[CollectionDefinition(nameof(RealServerDeliveryCollection), DisableParallelization = true)]
public sealed class RealServerDeliveryCollection;
