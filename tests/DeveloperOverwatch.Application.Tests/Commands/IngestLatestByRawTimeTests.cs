namespace DeveloperOverwatch.Application.Tests.Commands;

using DeveloperOverwatch.Application.Commands.IngestSessionEvents;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

/// <summary>
/// A fast client clock stamps events in the future, and the handler clamps stored times to now.
/// The latest event must still be chosen by its raw time: clamping first turns every future event
/// into a tie, and the first one in the batch would win.
/// </summary>
public class IngestLatestByRawTimeTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private readonly ISessionRepository _repo = Substitute.For<ISessionRepository>();
    private readonly Session _session = new() { Id = "s-raw" };

    public IngestLatestByRawTimeTests()
    {
        _repo.GetByIdAsync(_session.Id, Arg.Any<CancellationToken>()).Returns(_session);
        _repo.GetExistingTurnIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());
        _repo.GetExistingToolFailureIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());
    }

    [Fact]
    public async Task Handle_TwoFutureActivityEvents_LaterRawTimeWinsAndIsClampedToNow()
    {
        var states = Enum.GetValues<ActivityState>();
        var earlier = states[0];
        var later = states[1];

        await Handler().Handle(new IngestSessionEventsCommand
        {
            SessionId = _session.Id,
            ActivityEvents =
            [
                new ActivityEventDto { State = earlier, At = Now.AddSeconds(30) },
                new ActivityEventDto { State = later, At = Now.AddSeconds(60) }
            ]
        }, CancellationToken.None);

        _session.ActivityState.Should().Be(later);
        _session.ActivityStateAt.Should().Be(Now);
    }

    [Fact]
    public async Task Handle_TwoFutureToolFailures_LaterRawTimeNamesTheLastErrorAndIsClampedToNow()
    {
        await Handler().Handle(new IngestSessionEventsCommand
        {
            SessionId = _session.Id,
            ToolFailures =
            [
                new ToolFailureDto { Id = "f-early", ToolName = "Earlier", At = Now.AddSeconds(30) },
                new ToolFailureDto { Id = "f-late", ToolName = "Later", At = Now.AddSeconds(60) }
            ]
        }, CancellationToken.None);

        _session.LastToolErrorName.Should().Be("Later");
        _session.LastToolErrorAt.Should().Be(Now);
    }

    private IngestSessionEventsCommandHandler Handler() => new(
        _repo,
        Substitute.For<IUnitOfWork>(),
        Substitute.For<ITokenPricingCalculator>(),
        Substitute.For<IGovernanceViolationRepository>(),
        new FixedTime(Now));

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
