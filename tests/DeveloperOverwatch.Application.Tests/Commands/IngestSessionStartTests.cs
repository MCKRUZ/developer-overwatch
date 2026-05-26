namespace DeveloperOverwatch.Application.Tests.Commands;

using DeveloperOverwatch.Application.Commands.IngestSessionStart;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class IngestSessionStartTests
{
    private readonly ISessionRepository _repo = Substitute.For<ISessionRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task Handle_ValidCommand_CreatesSession()
    {
        var handler = new IngestSessionStartCommandHandler(_repo, _uow);
        var command = new IngestSessionStartCommand
        {
            SessionId = Guid.NewGuid().ToString(),
            DeveloperEmail = "dev@team.dev",
            DeveloperName = "Dev",
            RepoPath = "/repo",
            StartedAt = DateTime.UtcNow
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(command.SessionId);
        await _repo.Received(1).AddAsync(
            Arg.Is<Session>(s =>
                s.Id == command.SessionId &&
                s.DeveloperEmail == command.DeveloperEmail &&
                s.Status == SessionStatus.Active),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateSession_ReturnsFail()
    {
        var sessionId = Guid.NewGuid().ToString();
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(new Session { Id = sessionId });

        var handler = new IngestSessionStartCommandHandler(_repo, _uow);
        var command = new IngestSessionStartCommand
        {
            SessionId = sessionId,
            DeveloperEmail = "dev@team.dev",
            StartedAt = DateTime.UtcNow
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already exists");
    }

    [Fact]
    public async Task Validator_MissingEmail_Fails()
    {
        var validator = new IngestSessionStartCommandValidator();
        var command = new IngestSessionStartCommand
        {
            SessionId = Guid.NewGuid().ToString(),
            DeveloperEmail = "",
            StartedAt = DateTime.UtcNow
        };

        var result = await validator.ValidateAsync(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DeveloperEmail");
    }
}
