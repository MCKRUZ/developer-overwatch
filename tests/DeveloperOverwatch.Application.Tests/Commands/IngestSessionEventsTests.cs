namespace DeveloperOverwatch.Application.Tests.Commands;

using DeveloperOverwatch.Application.Commands.IngestSessionEvents;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class IngestSessionEventsTests
{
    private readonly ISessionRepository _repo = Substitute.For<ISessionRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task Handle_WithToolExecutions_AddsToSession()
    {
        var sessionId = Guid.NewGuid().ToString();
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(new Session { Id = sessionId, Status = SessionStatus.Active });

        var handler = new IngestSessionEventsCommandHandler(_repo, _uow);
        var command = new IngestSessionEventsCommand
        {
            SessionId = sessionId,
            ToolExecutions =
            [
                new ToolExecutionDto
                {
                    Id = Guid.NewGuid().ToString(),
                    ToolName = "Edit",
                    InputParams = "{\"file\":\"test.cs\"}",
                    DurationMs = 150,
                    Timestamp = DateTime.UtcNow
                }
            ],
            FileChanges =
            [
                new FileChangeDto
                {
                    Id = Guid.NewGuid().ToString(),
                    FilePath = "test.cs",
                    ChangeType = ChangeType.Modified,
                    DiffText = "+line1\n-line2"
                }
            ]
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _repo.Received(1).AddToolExecutionsAsync(
            Arg.Is<IEnumerable<ToolExecution>>(t => t.Count() == 1),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).AddFileChangesAsync(
            Arg.Is<IEnumerable<FileChange>>(f => f.Count() == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SessionNotFound_ReturnsFail()
    {
        _repo.GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Session?)null);

        var handler = new IngestSessionEventsCommandHandler(_repo, _uow);
        var command = new IngestSessionEventsCommand
        {
            SessionId = "nonexistent",
            ToolExecutions = [],
            FileChanges = []
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }
}
