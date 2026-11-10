namespace DeveloperOverwatch.Application.Tests.Commands;

using DeveloperOverwatch.Application.Commands.IngestSessionEnd;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class IngestSessionEndTests
{
    private readonly ISessionRepository _repo = Substitute.For<ISessionRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task Handle_ActiveSession_CompletesIt()
    {
        var sessionId = Guid.NewGuid().ToString();
        var session = new Session { Id = sessionId, Status = SessionStatus.Active };
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);

        var handler = new IngestSessionEndCommandHandler(_repo, _uow);
        var command = new IngestSessionEndCommand
        {
            SessionId = sessionId,
            EndedAt = DateTime.UtcNow,
            SummaryJson = "{\"turns\":5,\"tools\":[\"Edit\",\"Bash\"]}"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClosedSession.Should().BeTrue();
        session.Status.Should().Be(SessionStatus.Completed);
        session.EndedAt.Should().NotBeNull();
        session.SummaryJson.Should().Be(command.SummaryJson);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SessionNotFound_ReturnsFail()
    {
        _repo.GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Session?)null);

        var handler = new IngestSessionEndCommandHandler(_repo, _uow);
        var command = new IngestSessionEndCommand
        {
            SessionId = "nonexistent",
            EndedAt = DateTime.UtcNow
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_AlreadyCompletedSession_SucceedsWithoutChangingIt()
    {
        var sessionId = Guid.NewGuid().ToString();
        var endedAt = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
        var session = new Session { Id = sessionId, Status = SessionStatus.Completed, EndedAt = endedAt, SummaryJson = "{}" };
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);

        var handler = new IngestSessionEndCommandHandler(_repo, _uow);
        var command = new IngestSessionEndCommand
        {
            SessionId = sessionId,
            EndedAt = DateTime.UtcNow
        };

        var result = await handler.Handle(command, CancellationToken.None);

        // A redelivered end must not dead-letter the Agent's group, so it succeeds and changes nothing.
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(
            new IngestSessionEndResult(DeveloperOverwatch.Application.Commands.IngestOutcome.Accepted, ClosedSession: false));
        session.EndedAt.Should().Be(endedAt);
        session.SummaryJson.Should().Be("{}");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NewEventIdForAlreadyCompletedSession_RecordsItWithoutClosingAgain()
    {
        var sessionId = Guid.NewGuid().ToString();
        var session = new Session { Id = sessionId, Status = SessionStatus.Completed };
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);
        var handler = new IngestSessionEndCommandHandler(_repo, _uow);

        var result = await handler.Handle(
            new IngestSessionEndCommand { SessionId = sessionId, EventId = "e1", EndedAt = DateTime.UtcNow },
            CancellationToken.None);

        result.Value.Should().Be(
            new IngestSessionEndResult(DeveloperOverwatch.Application.Commands.IngestOutcome.Accepted, ClosedSession: false));
        await _repo.Received(1).AddProcessedAgentEventAsync(
            Arg.Is<ProcessedAgentEvent>(e => e.EventId == "e1"), Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().UpdateAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>());
    }
}
