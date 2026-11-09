namespace DeveloperOverwatch.Application.Tests.Queries;

using DeveloperOverwatch.Application.Queries.GetSessions;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class GetSessionsTests
{
    private readonly ISessionRepository _repo = Substitute.For<ISessionRepository>();
    private readonly ISessionStaleThresholdProvider _staleThreshold = Substitute.For<ISessionStaleThresholdProvider>();

    public GetSessionsTests() => _staleThreshold.StaleAfterMinutes.Returns(30);

    [Fact]
    public async Task Handle_PassesDateAndStatusFiltersToRepository()
    {
        var from = new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc);
        _repo.GetAllAsync("dev@team.dev", "owner/repo", from, to, SessionStatus.Active, Arg.Any<CancellationToken>())
            .Returns(new List<Session>());
        _repo.GetViolationCountsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());

        var handler = new GetSessionsQueryHandler(_repo, _staleThreshold);
        var query = new GetSessionsQuery("dev@team.dev", "owner/repo", from, to, SessionStatus.Active);

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _repo.Received(1).GetAllAsync(
            "dev@team.dev", "owner/repo", from, to, SessionStatus.Active, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MapsViolationCountsOntoEachSessionItem()
    {
        var sessions = new List<Session>
        {
            new() { Id = "s1", DeveloperEmail = "dev@team.dev", EstimatedCostUsd = 1.5m },
            new() { Id = "s2", DeveloperEmail = "dev@team.dev" }
        };
        _repo.GetAllAsync(null, null, null, null, null, Arg.Any<CancellationToken>()).Returns(sessions);
        _repo.GetViolationCountsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int> { ["s1"] = 3 });

        var handler = new GetSessionsQueryHandler(_repo, _staleThreshold);
        var result = await handler.Handle(new GetSessionsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var items = result.Value!;
        items.Single(i => i.Id == "s1").ViolationCount.Should().Be(3);
        items.Single(i => i.Id == "s1").EstimatedCostUsd.Should().Be(1.5m);
        items.Single(i => i.Id == "s2").ViolationCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_BucketsUsageRowsByCalendarDayPerSession_SeparateFromLifetimeTotal()
    {
        var sessions = new List<Session>
        {
            // Lifetime total spans both days — the per-day breakdown should NOT just repeat this.
            new() { Id = "s1", DeveloperEmail = "dev@team.dev", EstimatedCostUsd = 100m },
        };
        _repo.GetAllAsync(null, null, null, null, null, Arg.Any<CancellationToken>()).Returns(sessions);
        _repo.GetViolationCountsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());
        _repo.GetTurnUsageRowsForSessionsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<SessionTurnUsageRow>
            {
                new("s1", new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc), 10, 5, 0, 0, 1.00m),
                new("s1", new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc), 10, 5, 0, 0, 1.00m),
                new("s1", new DateTime(2026, 9, 21, 8, 0, 0, DateTimeKind.Utc), 20, 10, 0, 0, 5.00m),
            });

        var handler = new GetSessionsQueryHandler(_repo, _staleThreshold);
        var result = await handler.Handle(new GetSessionsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var item = result.Value!.Single(i => i.Id == "s1");
        item.EstimatedCostUsd.Should().Be(100m); // lifetime total untouched
        item.DailyUsage.Should().HaveCount(2);
        item.DailyUsage["2026-09-20"].EstimatedCostUsd.Should().Be(2.00m);
        item.DailyUsage["2026-09-20"].InputTokens.Should().Be(20);
        item.DailyUsage["2026-09-21"].EstimatedCostUsd.Should().Be(5.00m);
    }

    [Fact]
    public async Task Handle_DailyUsage_CarriesCacheTokensSoTheListTotalMatchesTheDetailPage()
    {
        // Cache reads are usually the bulk of Claude Code usage; a per-day total without them
        // disagrees with the session detail page and badly understates real volume.
        _repo.GetAllAsync(null, null, null, null, null, Arg.Any<CancellationToken>())
            .Returns(new List<Session> { new() { Id = "s1", DeveloperEmail = "dev@team.dev" } });
        _repo.GetViolationCountsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());
        _repo.GetTurnUsageRowsForSessionsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<SessionTurnUsageRow>
            {
                new("s1", new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc), 1, 5, 300, 9000, 1.00m),
                new("s1", new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc), 2, 5, 100, 1000, 1.00m),
            });

        var handler = new GetSessionsQueryHandler(_repo, _staleThreshold);
        var result = await handler.Handle(new GetSessionsQuery(), CancellationToken.None);

        var day = result.Value!.Single().DailyUsage["2026-09-20"];
        day.CacheCreationInputTokens.Should().Be(400);
        day.CacheReadInputTokens.Should().Be(10000);
    }

    [Fact]
    public async Task Handle_SessionWithNoUsageRows_HasEmptyDailyUsage()
    {
        var sessions = new List<Session> { new() { Id = "s1", DeveloperEmail = "dev@team.dev" } };
        _repo.GetAllAsync(null, null, null, null, null, Arg.Any<CancellationToken>()).Returns(sessions);
        _repo.GetViolationCountsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());
        _repo.GetTurnUsageRowsForSessionsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<SessionTurnUsageRow>());

        var handler = new GetSessionsQueryHandler(_repo, _staleThreshold);
        var result = await handler.Handle(new GetSessionsQuery(), CancellationToken.None);

        result.Value!.Single(i => i.Id == "s1").DailyUsage.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ActiveSessionPastStaleThreshold_ReportsStatusAsStale()
    {
        var sessions = new List<Session>
        {
            new()
            {
                Id = "s1", DeveloperEmail = "dev@team.dev", Status = SessionStatus.Active,
                LastActivityAt = DateTime.UtcNow.AddMinutes(-45),
            },
        };
        _repo.GetAllAsync(null, null, null, null, null, Arg.Any<CancellationToken>()).Returns(sessions);
        _repo.GetViolationCountsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());

        var handler = new GetSessionsQueryHandler(_repo, _staleThreshold);
        var result = await handler.Handle(new GetSessionsQuery(), CancellationToken.None);

        result.Value!.Single(i => i.Id == "s1").Status.Should().Be(SessionStatus.Stale);
    }

    [Fact]
    public async Task Handle_ActiveSessionWithinStaleThreshold_StaysActive()
    {
        var sessions = new List<Session>
        {
            new()
            {
                Id = "s1", DeveloperEmail = "dev@team.dev", Status = SessionStatus.Active,
                LastActivityAt = DateTime.UtcNow.AddMinutes(-5),
            },
        };
        _repo.GetAllAsync(null, null, null, null, null, Arg.Any<CancellationToken>()).Returns(sessions);
        _repo.GetViolationCountsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());

        var handler = new GetSessionsQueryHandler(_repo, _staleThreshold);
        var result = await handler.Handle(new GetSessionsQuery(), CancellationToken.None);

        result.Value!.Single(i => i.Id == "s1").Status.Should().Be(SessionStatus.Active);
    }

    [Fact]
    public async Task Handle_CompletedSessionPastThreshold_StaysCompleted_NeverBecomesStale()
    {
        var sessions = new List<Session>
        {
            new()
            {
                Id = "s1", DeveloperEmail = "dev@team.dev", Status = SessionStatus.Completed,
                LastActivityAt = DateTime.UtcNow.AddDays(-30),
            },
        };
        _repo.GetAllAsync(null, null, null, null, null, Arg.Any<CancellationToken>()).Returns(sessions);
        _repo.GetViolationCountsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());

        var handler = new GetSessionsQueryHandler(_repo, _staleThreshold);
        var result = await handler.Handle(new GetSessionsQuery(), CancellationToken.None);

        result.Value!.Single(i => i.Id == "s1").Status.Should().Be(SessionStatus.Completed);
    }

    [Fact]
    public async Task Handle_FilteringByActive_ExcludesStaleSessions()
    {
        var sessions = new List<Session>
        {
            new()
            {
                Id = "genuinely-active", DeveloperEmail = "dev@team.dev", Status = SessionStatus.Active,
                LastActivityAt = DateTime.UtcNow.AddMinutes(-1),
            },
            new()
            {
                Id = "stale", DeveloperEmail = "dev@team.dev", Status = SessionStatus.Active,
                LastActivityAt = DateTime.UtcNow.AddHours(-2),
            },
        };
        _repo.GetAllAsync(null, null, null, null, SessionStatus.Active, Arg.Any<CancellationToken>()).Returns(sessions);
        _repo.GetViolationCountsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());

        var handler = new GetSessionsQueryHandler(_repo, _staleThreshold);
        var result = await handler.Handle(new GetSessionsQuery(Status: SessionStatus.Active), CancellationToken.None);

        result.Value!.Should().ContainSingle().Which.Id.Should().Be("genuinely-active");
    }

    [Fact]
    public async Task Handle_FilteringByStale_MapsToActiveDbQueryThenReturnsOnlyStaleOnes()
    {
        var sessions = new List<Session>
        {
            new()
            {
                Id = "genuinely-active", DeveloperEmail = "dev@team.dev", Status = SessionStatus.Active,
                LastActivityAt = DateTime.UtcNow.AddMinutes(-1),
            },
            new()
            {
                Id = "stale", DeveloperEmail = "dev@team.dev", Status = SessionStatus.Active,
                LastActivityAt = DateTime.UtcNow.AddHours(-2),
            },
        };
        // "Stale" is never stored — filtering for it must query the DB for Active rows.
        _repo.GetAllAsync(null, null, null, null, SessionStatus.Active, Arg.Any<CancellationToken>()).Returns(sessions);
        _repo.GetViolationCountsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, int>());

        var handler = new GetSessionsQueryHandler(_repo, _staleThreshold);
        var result = await handler.Handle(new GetSessionsQuery(Status: SessionStatus.Stale), CancellationToken.None);

        result.Value!.Should().ContainSingle().Which.Id.Should().Be("stale");
    }
}
