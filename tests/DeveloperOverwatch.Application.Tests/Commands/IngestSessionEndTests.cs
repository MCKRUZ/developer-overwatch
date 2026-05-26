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
    public async Task Handle_AlreadyCompletedSession_ReturnsFail()
    {
        var sessionId = Guid.NewGuid().ToString();
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(new Session { Id = sessionId, Status = SessionStatus.Completed });

        var handler = new IngestSessionEndCommandHandler(_repo, _uow);
        var command = new IngestSessionEndCommand
        {
            SessionId = sessionId,
            EndedAt = DateTime.UtcNow
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not active");
    }
}
