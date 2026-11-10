using System.Collections.Concurrent;
using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit.Abstractions;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>The delivery contract's invariants, proved end to end: the real queue, ApiClient and processor
/// against a fake server with the real server's idempotency and session-status rules, under seeded faults,
/// including the agent dying between a send and its mark and between a cursor write and its marker's mark,
/// and a day passing so every shipped row is purged. Every item must be stored exactly once; only the
/// poison turn may be lost; nothing from before enrolment may leave the machine except backfill's
/// usage-only turns; and the queue must drain down to the held turn's items, which keep being retried.
/// Per-item rejections inside a 200 are covered by the shippers' unit tests: here the poison's 400 is the
/// only loss. The class runs alone, so its wall-clock bound measures the agent, not other tests.</summary>
[Collection(nameof(DeliveryInvariantCollection))]
public sealed class DeliveryInvariantTests(ITestOutputHelper output)
{
    private static readonly int[] SeedList = [20261109, 7, 42, 101, 1337, 2024, 31337, 65537, 99991, 123456, 271828, 314159];

    private static readonly string[] CarriedLists =
        ["conversationTurns", "toolExecutions", "fileChanges", "toolFailures", "activityEvents"];

    /// <summary>Each seed runs once per test process; the union fact reuses the theory's runs.</summary>
    private static readonly ConcurrentDictionary<int, Lazy<Task<InvariantResult>>> Runs = new();

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        foreach (var seed in SeedList)
            data.Add(seed);
        return data;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task UnderSeededFaults_StoreEveryItemOnce_LoseOnlyThePoison_SendNothingPreEnrolment_AndDrainToTheHeldTurn(int seed)
    {
        var run = await RunAsync(seed);
        output.WriteLine(run.Summary());
        var server = run.Server;
        var workload = run.Workload;

        using (new AssertionScope())
        {
            var keys = server.Rows.Select(r => r.Key).ToList();
            keys.Should().OnlyHaveUniqueItems("the server's dedupe keys admit each item once");
            keys.Should().BeEquivalentTo(workload.ExpectedRows, "a missing row is a lost item, and an extra one an unstable id resent");

            server.Rows.Should().NotContain(r => r.Id == InvariantWorkload.PoisonId);
            run.Fired.Should().ContainKey(InvariantFault.PoisonRejectedAlone, "the poison is lost only once it is refused alone");

            ForbiddenSent(run).Should().BeEmpty("nothing from before pilotSince may leave the machine");

            server.Attempts.SelectMany(a => a.Items)
                .Where(i => i.Kind == "turn" && workload.PreTurnIds.Contains(i.Id))
                .Should().OnlyContain(i => workload.BackfillTurnIds.Contains(i.Id) && !i.HasText,
                    "a pre-enrolment turn leaves only through backfill, usage only");
            server.TurnsStoredWithText.Should().NotIntersectWith(workload.BackfillTurnIds);

            run.PendingAtExit.Should().BeEquivalentTo(workload.HeldPending, "the queue drains down to the held turn's items");
            run.PendingAfterSoak.Should().BeEquivalentTo(run.PendingAtExit, "the held items are never dropped");
            run.SoakHeldAttempts.Should().BePositive("a held turn is retried once its backoff is over");

            server.Sessions.Should().BeEquivalentTo(workload.ExpectedSessions());

            server.Attempts.Where(a => a.Path.EndsWith("/events", StringComparison.Ordinal))
                .Should().OnlyContain(a => CarriesSomething(a.Body),
                    "an /events POST that carries no item and no activity state is a wasted request");

            run.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
        }
    }

    [Fact]
    public async Task AcrossTheSeeds_EveryFaultKindFires()
    {
        var fired = new HashSet<InvariantFault>();
        foreach (var seed in SeedList)
            fired.UnionWith((await RunAsync(seed)).Fired.Keys);

        fired.Should().BeEquivalentTo(Enum.GetValues<InvariantFault>(), "the runs prove something under every fault only if each one fired");
    }

    private static Task<InvariantResult> RunAsync(int seed) =>
        Runs.GetOrAdd(seed, s => new Lazy<Task<InvariantResult>>(() => InvariantRun.RunAsync(s))).Value;

    private static List<string> ForbiddenSent(InvariantResult run) =>
        run.Server.Attempts
            .SelectMany(a => run.Workload.Forbidden
                .Where(f => a.Path.Contains(f, StringComparison.Ordinal) || a.Body.Contains(f, StringComparison.Ordinal))
                .Select(f => $"poll {a.Poll} {a.Path}: {f}"))
            .ToList();

    private static bool CarriesSomething(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return CarriedLists.Sum(list => doc.RootElement.TryGetProperty(list, out var items) ? items.GetArrayLength() : 0) > 0;
    }
}

/// <summary>Runs <see cref="DeliveryInvariantTests"/> after every parallel test class, with nothing beside it.</summary>
[CollectionDefinition(nameof(DeliveryInvariantCollection), DisableParallelization = true)]
public sealed class DeliveryInvariantCollection;
