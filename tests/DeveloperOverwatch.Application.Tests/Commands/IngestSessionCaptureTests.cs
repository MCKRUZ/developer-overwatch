namespace DeveloperOverwatch.Application.Tests.Commands;

using DeveloperOverwatch.Application.Commands.IngestSessionEvents;
using DeveloperOverwatch.Application.Commands.IngestSessionStart;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

/// <summary>
/// The capture fields (agent, machine, project, branch, subagent, context size, activity state,
/// tool failures) that the Agent sends so the token meter can attribute and explain usage.
/// </summary>
public class IngestSessionCaptureTests
{
    private static readonly DateTime T0 = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private readonly ISessionRepository _repo = Substitute.For<ISessionRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ITokenPricingCalculator _pricing = Substitute.For<ITokenPricingCalculator>();
    private readonly IGovernanceViolationRepository _violations = Substitute.For<IGovernanceViolationRepository>();

    public IngestSessionCaptureTests()
    {
        _repo.GetExistingTurnIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>());
    }

    [Fact]
    public async Task Start_NewSessionWithCaptureFields_StoresAgentMachineAndProject()
    {
        Session? stored = null;
        await _repo.AddAsync(Arg.Do<Session>(s => stored = s), Arg.Any<CancellationToken>());

        await StartHandler().Handle(StartCommand("s-1") with
        {
            Agent = "codex",
            MachineName = "build-box-7",
            ProjectKey = "github.com/mckruz/overwatch"
        }, CancellationToken.None);

        stored.Should().NotBeNull();
        stored!.Agent.Should().Be("codex");
        stored.MachineName.Should().Be("build-box-7");
        stored.ProjectKey.Should().Be("github.com/mckruz/overwatch");
    }

    [Fact]
    public async Task Start_NewSessionWithoutAgent_DefaultsToClaudeCode()
    {
        Session? stored = null;
        await _repo.AddAsync(Arg.Do<Session>(s => stored = s), Arg.Any<CancellationToken>());

        await StartHandler().Handle(StartCommand("s-2"), CancellationToken.None);

        stored!.Agent.Should().Be("claude-code");
    }

    [Fact]
    public async Task Start_ResumedSession_FillsOnlyMissingMachineAndProject()
    {
        var existing = new Session { Id = "s-3", MachineName = "box-a", ProjectKey = null };
        _repo.GetByIdAsync("s-3", Arg.Any<CancellationToken>()).Returns(existing);

        await StartHandler().Handle(StartCommand("s-3") with
        {
            MachineName = "box-b",
            ProjectKey = "github.com/o/r"
        }, CancellationToken.None);

        existing.MachineName.Should().Be("box-a");
        existing.ProjectKey.Should().Be("github.com/o/r");
    }

    [Theory]
    [InlineData("https://user:token@github.com/o/r")]
    [InlineData("HTTPS://token@github.com/o/r")]
    public void StartValidator_ProjectKeyWithUrlCredentials_Fails(string projectKey)
    {
        var result = new IngestSessionStartCommandValidator()
            .Validate(StartCommand("s-4") with { ProjectKey = projectKey });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(IngestSessionStartCommand.ProjectKey));
    }

    [Theory]
    [InlineData("github.com/o/r")]
    [InlineData("https://github.com/o/r")]
    public void StartValidator_ProjectKeyWithoutCredentials_Passes(string projectKey)
    {
        var result = new IngestSessionStartCommandValidator()
            .Validate(StartCommand("s-5") with { ProjectKey = projectKey });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Events_Turns_StoreBranchSubagentAndComputedContextTokens()
    {
        var session = ExistingSession("s-6");
        List<ConversationTurn> stored = [];
        await _repo.AddTurnsAsync(
            Arg.Do<IEnumerable<ConversationTurn>>(ts => stored = ts.ToList()), Arg.Any<CancellationToken>());

        await EventsHandler().Handle(new IngestSessionEventsCommand
        {
            SessionId = session.Id,
            ConversationTurns =
            [
                new ConversationTurnDto { Id = "u-1", Role = "user", Timestamp = T0 },
                new ConversationTurnDto
                {
                    Id = "a-1", Role = "assistant", Timestamp = T0,
                    InputTokens = 10, CacheReadInputTokens = 1000, CacheCreationInputTokens = 200, OutputTokens = 50,
                    GitBranch = "feat/meter", SubagentName = "explorer"
                }
            ]
        }, CancellationToken.None);

        stored.Single(t => t.Id == "u-1").ContextTokens.Should().BeNull();
        var assistant = stored.Single(t => t.Id == "a-1");
        assistant.ContextTokens.Should().Be(1210);
        assistant.GitBranch.Should().Be("feat/meter");
        assistant.SubagentName.Should().Be("explorer");
        session.PeakContextTokens.Should().Be(1210);
    }

    [Fact]
    public async Task Events_TwoIngests_PeakContextTokensIsMaxAndNeverDecreases()
    {
        var session = ExistingSession("s-7");
        var handler = EventsHandler();

        await handler.Handle(TurnsCommand(session.Id, ("a-1", 3000), ("a-2", 5000)), CancellationToken.None);
        session.PeakContextTokens.Should().Be(5000);

        await handler.Handle(TurnsCommand(session.Id, ("a-3", 2000)), CancellationToken.None);
        session.PeakContextTokens.Should().Be(5000);
    }

    [Fact]
    public async Task Events_ActivityEventOlderThanStored_IsIgnored()
    {
        var session = ExistingSession("s-8");
        session.ActivityState = ActivityState.Waiting;
        session.ActivityStateAt = T0;

        await EventsHandler().Handle(new IngestSessionEventsCommand
        {
            SessionId = session.Id,
            ActivityEvents = [new ActivityEventDto { State = ActivityState.Active, At = T0.AddSeconds(-1) }]
        }, CancellationToken.None);

        session.ActivityState.Should().Be(ActivityState.Waiting);
        session.ActivityStateAt.Should().Be(T0);
    }

    [Fact]
    public async Task Events_NewerActivityEvents_ApplyTheLatestByTimestamp()
    {
        var session = ExistingSession("s-9");
        session.ActivityState = ActivityState.Waiting;
        session.ActivityStateAt = T0;

        await EventsHandler().Handle(new IngestSessionEventsCommand
        {
            SessionId = session.Id,
            ActivityEvents =
            [
                new ActivityEventDto { State = ActivityState.Active, At = T0.AddSeconds(2) },
                new ActivityEventDto { State = ActivityState.Idle, At = T0.AddSeconds(1) }
            ]
        }, CancellationToken.None);

        session.ActivityState.Should().Be(ActivityState.Active);
        session.ActivityStateAt.Should().Be(T0.AddSeconds(2));
    }

    [Fact]
    public async Task Events_ToolFailures_AreCountedAndLatestNameKept()
    {
        var session = ExistingSession("s-10");
        session.ToolErrorCount = 2;
        session.LastToolErrorName = "Read";
        session.LastToolErrorAt = T0;

        await EventsHandler().Handle(new IngestSessionEventsCommand
        {
            SessionId = session.Id,
            ToolFailures =
            [
                new ToolFailureDto { ToolName = "Bash", At = T0.AddMinutes(5) },
                new ToolFailureDto { ToolName = "Edit", At = T0.AddMinutes(1) }
            ]
        }, CancellationToken.None);

        session.ToolErrorCount.Should().Be(4);
        session.LastToolErrorName.Should().Be("Bash");
        session.LastToolErrorAt.Should().Be(T0.AddMinutes(5));
    }

    private IngestSessionStartCommandHandler StartHandler() => new(_repo, _uow);

    private IngestSessionEventsCommandHandler EventsHandler() => new(_repo, _uow, _pricing, _violations);

    private static IngestSessionStartCommand StartCommand(string sessionId) => new()
    {
        SessionId = sessionId,
        DeveloperEmail = "dev@team.dev",
        StartedAt = T0
    };

    private Session ExistingSession(string sessionId)
    {
        var session = new Session { Id = sessionId, Status = SessionStatus.Active };
        _repo.GetByIdAsync(sessionId, Arg.Any<CancellationToken>()).Returns(session);
        return session;
    }

    private static IngestSessionEventsCommand TurnsCommand(string sessionId, params (string Id, int Input)[] turns) => new()
    {
        SessionId = sessionId,
        ConversationTurns = turns
            .Select(t => new ConversationTurnDto { Id = t.Id, Role = "assistant", InputTokens = t.Input, Timestamp = T0 })
            .ToList()
    };
}
