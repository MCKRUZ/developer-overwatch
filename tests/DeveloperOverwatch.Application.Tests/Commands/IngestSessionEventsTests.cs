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
    private readonly ITokenPricingCalculator _pricing = Substitute.For<ITokenPricingCalculator>();
    private readonly IGovernanceViolationRepository _violations = Substitute.For<IGovernanceViolationRepository>();

    [Fact]
    public async Task Handle_WithToolExecutions_AddsToSession()
    {
        var sessionId = Guid.NewGuid().ToString();
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>())
            .Returns(new Session { Id = sessionId, Status = SessionStatus.Active });

        var handler = new IngestSessionEventsCommandHandler(_repo, _uow, _pricing, _violations);
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

        var handler = new IngestSessionEventsCommandHandler(_repo, _uow, _pricing, _violations);
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

    [Fact]
    public async Task Handle_WithConversationTurns_AggregatesTokensAndCostOnSession()
    {
        var sessionId = Guid.NewGuid().ToString();
        var session = new Session { Id = sessionId, Status = SessionStatus.Active };
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);
        _pricing.CalculateCost(null, 15, 8, null, null).Returns(0.001m);

        var handler = new IngestSessionEventsCommandHandler(_repo, _uow, _pricing, _violations);
        var command = new IngestSessionEventsCommand
        {
            SessionId = sessionId,
            ConversationTurns =
            [
                new ConversationTurnDto
                {
                    Id = Guid.NewGuid().ToString(),
                    Role = "assistant",
                    Content = "hi",
                    InputTokens = 15,
                    OutputTokens = 8,
                    Timestamp = DateTime.UtcNow
                }
            ]
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        session.TotalInputTokens.Should().Be(15);
        session.TotalOutputTokens.Should().Be(8);
        session.EstimatedCostUsd.Should().Be(0.001m);
        await _repo.Received(1).UpdateAsync(session, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TurnFlaggedAsContainingRedactedSecret_CreatesCriticalViolation()
    {
        var sessionId = Guid.NewGuid().ToString();
        var session = new Session { Id = sessionId, Status = SessionStatus.Active };
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);

        var handler = new IngestSessionEventsCommandHandler(_repo, _uow, _pricing, _violations);
        var command = new IngestSessionEventsCommand
        {
            SessionId = sessionId,
            ConversationTurns =
            [
                new ConversationTurnDto
                {
                    Id = Guid.NewGuid().ToString(),
                    TurnIndex = 3,
                    Role = "user",
                    Content = "here's my [REDACTED]",
                    ContainsRedactedSecret = true,
                    Timestamp = DateTime.UtcNow
                }
            ]
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _violations.Received(1).AddRangeAsync(
            Arg.Is<IEnumerable<GovernanceViolation>>(v =>
                v.Count() == 1 &&
                v.First().SessionId == sessionId &&
                v.First().Severity == ViolationSeverity.Critical &&
                v.First().RuleId == "agent-redacted-secret"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_IgnoresClientSentTurnIndex_AssignsSequentialIndexFromExistingCount()
    {
        // The Agent's TurnIndex is derived from its transcript-line cursor (thousands of lines
        // for a session with only dozens of real turns) — the server must re-number turns
        // sequentially from the session's actual persisted turn count instead of trusting it.
        var sessionId = Guid.NewGuid().ToString();
        var session = new Session
        {
            Id = sessionId,
            Status = SessionStatus.Active,
            ConversationTurns =
            [
                new ConversationTurn { Id = "existing-1", SessionId = sessionId, TurnIndex = 0, Role = "user", Content = "a", Timestamp = DateTime.UtcNow },
                new ConversationTurn { Id = "existing-2", SessionId = sessionId, TurnIndex = 1, Role = "assistant", Content = "b", Timestamp = DateTime.UtcNow },
            ],
        };
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);

        ConversationTurn? added = null;
        await _repo.AddTurnsAsync(
            Arg.Do<IEnumerable<ConversationTurn>>(ts => added = ts.Single()),
            Arg.Any<CancellationToken>());

        var handler = new IngestSessionEventsCommandHandler(_repo, _uow, _pricing, _violations);
        var command = new IngestSessionEventsCommand
        {
            SessionId = sessionId,
            ConversationTurns =
            [
                new ConversationTurnDto
                {
                    Id = "new-turn",
                    TurnIndex = 960, // bogus client-sent line-cursor value — must be ignored
                    Role = "assistant",
                    Content = "here's my [REDACTED]",
                    ContainsRedactedSecret = true,
                    Timestamp = DateTime.UtcNow,
                },
            ],
        };

        await handler.Handle(command, CancellationToken.None);

        added!.TurnIndex.Should().Be(2);
        await _violations.Received(1).AddRangeAsync(
            Arg.Is<IEnumerable<GovernanceViolation>>(v =>
                v.Single().Message == "Agent redacted a likely secret in conversation turn 2"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithSessionTitle_UpdatesSessionTitleWhenChanged()
    {
        var sessionId = Guid.NewGuid().ToString();
        var session = new Session { Id = sessionId, Status = SessionStatus.Active, Title = "old-name" };
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);

        var handler = new IngestSessionEventsCommandHandler(_repo, _uow, _pricing, _violations);
        var command = new IngestSessionEventsCommand
        {
            SessionId = sessionId,
            SessionTitle = "renamed-mid-session"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        session.Title.Should().Be("renamed-mid-session");
        await _repo.Received(1).UpdateAsync(session, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RedeliveredToolExecutionId_IsIgnoredNotDuplicated()
    {
        // The Agent delivers at-least-once — a slow response can look like a failure client-side
        // even though the batch already committed, so the same Id gets resubmitted on retry.
        var sessionId = Guid.NewGuid().ToString();
        var session = new Session
        {
            Id = sessionId,
            Status = SessionStatus.Active,
            ToolExecutions = [new ToolExecution { Id = "tool-1", SessionId = sessionId, ToolName = "Read", Timestamp = DateTime.UtcNow }],
        };
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);

        var handler = new IngestSessionEventsCommandHandler(_repo, _uow, _pricing, _violations);
        var command = new IngestSessionEventsCommand
        {
            SessionId = sessionId,
            ToolExecutions = [new ToolExecutionDto { Id = "tool-1", ToolName = "Read", Timestamp = DateTime.UtcNow }],
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
        await _repo.DidNotReceive().AddToolExecutionsAsync(
            Arg.Any<IEnumerable<ToolExecution>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RedeliveredConversationTurnId_DoesNotDoubleCountCost()
    {
        var sessionId = Guid.NewGuid().ToString();
        var session = new Session
        {
            Id = sessionId,
            Status = SessionStatus.Active,
            TotalInputTokens = 100,
            EstimatedCostUsd = 1.50m,
            ConversationTurns = [new ConversationTurn
            {
                Id = "turn-1", SessionId = sessionId, TurnIndex = 0, Role = "user", Content = "hi", Timestamp = DateTime.UtcNow,
            }],
        };
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);
        _repo.GetExistingTurnIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string> { "turn-1" });
        _pricing.CalculateCost(Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>())
            .Returns(1.50m);

        var handler = new IngestSessionEventsCommandHandler(_repo, _uow, _pricing, _violations);
        var command = new IngestSessionEventsCommand
        {
            SessionId = sessionId,
            ConversationTurns = [new ConversationTurnDto
            {
                Id = "turn-1", TurnIndex = 0, Role = "user", Content = "hi", InputTokens = 100, Timestamp = DateTime.UtcNow,
            }],
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
        session.TotalInputTokens.Should().Be(100);
        session.EstimatedCostUsd.Should().Be(1.50m);
        await _repo.DidNotReceive().AddTurnsAsync(
            Arg.Any<IEnumerable<ConversationTurn>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithoutSessionTitle_LeavesExistingTitleUnchanged()
    {
        var sessionId = Guid.NewGuid().ToString();
        var session = new Session { Id = sessionId, Status = SessionStatus.Active, Title = "keep-me" };
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);

        var handler = new IngestSessionEventsCommandHandler(_repo, _uow, _pricing, _violations);
        var command = new IngestSessionEventsCommand { SessionId = sessionId };

        await handler.Handle(command, CancellationToken.None);

        session.Title.Should().Be("keep-me");
    }

    [Fact]
    public async Task Handle_TurnIdAlreadyStoredUnderAnotherSession_IsSkippedAndNotCounted()
    {
        // A resumed session copies the earlier session's transcript lines — same ids — into its
        // own file. Those turns were already counted once, under the original session.
        var sessionId = Guid.NewGuid().ToString();
        var session = new Session { Id = sessionId, Status = SessionStatus.Active };
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);
        _repo.GetExistingTurnIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string> { "copied-turn" });
        _pricing.CalculateCost(Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>())
            .Returns(0.25m);

        var handler = new IngestSessionEventsCommandHandler(_repo, _uow, _pricing, _violations);
        var command = new IngestSessionEventsCommand
        {
            SessionId = sessionId,
            ConversationTurns =
            [
                new ConversationTurnDto { Id = "copied-turn", Role = "assistant", InputTokens = 1000, Timestamp = DateTime.UtcNow },
                new ConversationTurnDto { Id = "fresh-turn", Role = "assistant", InputTokens = 7, Timestamp = DateTime.UtcNow },
            ],
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.Value.Should().Be(1);
        session.TotalInputTokens.Should().Be(7);
        session.EstimatedCostUsd.Should().Be(0.25m);
        await _repo.Received(1).AddTurnsAsync(
            Arg.Is<IEnumerable<ConversationTurn>>(ts => ts.Single().Id == "fresh-turn"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SameTurnIdTwiceInOneBatch_CountsItOnce()
    {
        var sessionId = Guid.NewGuid().ToString();
        var session = new Session { Id = sessionId, Status = SessionStatus.Active };
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);
        _repo.GetExistingTurnIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());

        var handler = new IngestSessionEventsCommandHandler(_repo, _uow, _pricing, _violations);
        var turn = new ConversationTurnDto { Id = "dup", Role = "assistant", InputTokens = 40, Timestamp = DateTime.UtcNow };
        var command = new IngestSessionEventsCommand { SessionId = sessionId, ConversationTurns = [turn, turn] };

        var result = await handler.Handle(command, CancellationToken.None);

        result.Value.Should().Be(1);
        session.TotalInputTokens.Should().Be(40);
    }
}
