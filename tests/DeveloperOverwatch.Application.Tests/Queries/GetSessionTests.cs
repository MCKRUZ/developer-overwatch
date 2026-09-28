namespace DeveloperOverwatch.Application.Tests.Queries;

using DeveloperOverwatch.Application.Queries.GetSession;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class GetSessionTests
{
    private readonly ISessionRepository _repo = Substitute.For<ISessionRepository>();
    private readonly ISessionStaleThresholdProvider _staleThreshold = Substitute.For<ISessionStaleThresholdProvider>();

    public GetSessionTests() => _staleThreshold.StaleAfterMinutes.Returns(30);

    [Fact]
    public async Task Handle_ActiveSessionPastStaleThreshold_ReportsStatusAsStale()
    {
        var session = new Session
        {
            Id = "s1", DeveloperEmail = "dev@team.dev", Status = SessionStatus.Active,
            LastActivityAt = DateTime.UtcNow.AddMinutes(-45),
        };
        _repo.GetDetailAsync("s1", Arg.Any<CancellationToken>())
            .Returns(new SessionDetailProjection(session, 0, 0, 0));

        var handler = new GetSessionQueryHandler(_repo, _staleThreshold);
        var result = await handler.Handle(new GetSessionQuery("s1"), CancellationToken.None);

        result.Value!.Status.Should().Be(SessionStatus.Stale);
    }

    [Fact]
    public async Task Handle_ActiveSessionWithinStaleThreshold_StaysActive()
    {
        var session = new Session
        {
            Id = "s1", DeveloperEmail = "dev@team.dev", Status = SessionStatus.Active,
            LastActivityAt = DateTime.UtcNow.AddMinutes(-5),
        };
        _repo.GetDetailAsync("s1", Arg.Any<CancellationToken>())
            .Returns(new SessionDetailProjection(session, 0, 0, 0));

        var handler = new GetSessionQueryHandler(_repo, _staleThreshold);
        var result = await handler.Handle(new GetSessionQuery("s1"), CancellationToken.None);

        result.Value!.Status.Should().Be(SessionStatus.Active);
    }
}
