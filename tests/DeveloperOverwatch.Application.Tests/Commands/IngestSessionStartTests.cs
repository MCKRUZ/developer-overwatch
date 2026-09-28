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
    public async Task Handle_WithTitle_SetsSessionTitleOnCreate()
    {
        var handler = new IngestSessionStartCommandHandler(_repo, _uow);
        var command = new IngestSessionStartCommand
        {
            SessionId = Guid.NewGuid().ToString(),
            DeveloperEmail = "dev@team.dev",
            Title = "initial-setup",
            StartedAt = DateTime.UtcNow
        };

        await handler.Handle(command, CancellationToken.None);

        await _repo.Received(1).AddAsync(
            Arg.Is<Session>(s => s.Title == "initial-setup"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingSessionResumedWithTitle_UpdatesTitle()
    {
        var sessionId = Guid.NewGuid().ToString();
        var existing = new Session { Id = sessionId, Status = SessionStatus.Completed, Title = null };
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>()).Returns(existing);

        var handler = new IngestSessionStartCommandHandler(_repo, _uow);
        var command = new IngestSessionStartCommand
        {
            SessionId = sessionId,
            DeveloperEmail = "dev@team.dev",
            Title = "renamed-later",
            StartedAt = DateTime.UtcNow
        };

        await handler.Handle(command, CancellationToken.None);

        existing.Title.Should().Be("renamed-later");
    }

    [Fact]
    public async Task Handle_ExistingCompletedSession_ReactivatesInstead()
    {
        // A resumed Claude Code session fires SessionStart again with the same session_id —
        // reactivate the existing (possibly Completed) session rather than rejecting it.
        var sessionId = Guid.NewGuid().ToString();
        var existing = new Session
        {
            Id = sessionId,
            Status = SessionStatus.Completed,
            EndedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>()).Returns(existing);

        var handler = new IngestSessionStartCommandHandler(_repo, _uow);
        var command = new IngestSessionStartCommand
        {
            SessionId = sessionId,
            DeveloperEmail = "dev@team.dev",
            StartedAt = DateTime.UtcNow
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(sessionId);
        existing.Status.Should().Be(SessionStatus.Active);
        existing.EndedAt.Should().BeNull();
        await _repo.Received(1).UpdateAsync(existing, Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
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
