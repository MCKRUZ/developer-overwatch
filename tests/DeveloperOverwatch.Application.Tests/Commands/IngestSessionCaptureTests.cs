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

        // Behaves like the database: ids added in one ingest are "existing" in the next.
        _repo.GetExistingToolFailureIdsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlySet<string>)ci.Arg<IReadOnlyCollection<string>>()
                .Where(_storedFailureIds.Contains).ToHashSet());
        _repo.AddToolFailuresAsync(
            Arg.Do<IEnumerable<SessionToolFailure>>(fs => _storedFailureIds.UnionWith(fs.Select(f => f.Id))),
            Arg.Any<CancellationToken>());
    }

    private readonly HashSet<string> _storedFailureIds = [];

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
    [InlineData("git@github.com:o/r")]
    [InlineData("token@github.com/o/r")]
    [InlineData("github.com/o/r@x")]
    public void StartValidator_ProjectKeyContainingAt_Fails(string projectKey)
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
                new ToolFailureDto { Id = "f-1", ToolName = "Bash", At = T0.AddMinutes(5) },
                new ToolFailureDto { Id = "f-2", ToolName = "Edit", At = T0.AddMinutes(1) }
            ]
        }, CancellationToken.None);

        session.ToolErrorCount.Should().Be(4);
        session.LastToolErrorName.Should().Be("Bash");
        session.LastToolErrorAt.Should().Be(T0.AddMinutes(5));
    }

    [Fact]
    public async Task Events_SameToolFailureSentTwice_CountsOnce()
    {
        var session = ExistingSession("s-11");
        var handler = EventsHandler();
        var command = FailuresCommand(session.Id, ("f-1", "Bash", T0));

        await handler.Handle(command, CancellationToken.None);
        var second = await handler.Handle(command, CancellationToken.None);

        session.ToolErrorCount.Should().Be(1);
        second.Value!.EventsIngested.Should().Be(0);
    }

    [Fact]
    public async Task Events_DuplicateToolFailuresInOneBatch_CountOnce()
    {
        var session = ExistingSession("s-12");
        List<SessionToolFailure> stored = [];
        await _repo.AddToolFailuresAsync(
            Arg.Do<IEnumerable<SessionToolFailure>>(fs => stored.AddRange(fs)), Arg.Any<CancellationToken>());

        await EventsHandler().Handle(
            FailuresCommand(session.Id, ("f-1", "Bash", T0), ("f-1", "Bash", T0)), CancellationToken.None);

        session.ToolErrorCount.Should().Be(1);
        stored.Should().ContainSingle(f => f.Id == "f-1" && f.SessionId == session.Id);
    }

    [Fact]
    public async Task Events_RedeliveredOlderFailure_DoesNotChangeLatestToolError()
    {
        var session = ExistingSession("s-13");
        var handler = EventsHandler();
        await handler.Handle(FailuresCommand(session.Id, ("f-old", "Read", T0)), CancellationToken.None);
        await handler.Handle(FailuresCommand(session.Id, ("f-new", "Bash", T0.AddMinutes(1))), CancellationToken.None);

        await handler.Handle(FailuresCommand(session.Id, ("f-old", "Read", T0)), CancellationToken.None);

        session.ToolErrorCount.Should().Be(2);
        session.LastToolErrorName.Should().Be("Bash");
    }

    [Fact]
    public async Task Events_SubagentTurnLargerThanMainTurns_DoesNotRaisePeak()
    {
        var session = ExistingSession("s-14");
        List<ConversationTurn> stored = [];
        await _repo.AddTurnsAsync(
            Arg.Do<IEnumerable<ConversationTurn>>(ts => stored = ts.ToList()), Arg.Any<CancellationToken>());

        await EventsHandler().Handle(new IngestSessionEventsCommand
        {
            SessionId = session.Id,
            ConversationTurns =
            [
                new ConversationTurnDto { Id = "a-1", Role = "assistant", InputTokens = 4000, Timestamp = T0 },
                new ConversationTurnDto { Id = "s-1", Role = "subagent", InputTokens = 90000, Timestamp = T0 }
            ]
        }, CancellationToken.None);

        session.PeakContextTokens.Should().Be(4000);
        stored.Single(t => t.Id == "s-1").ContextTokens.Should().Be(90000);
    }

    [Fact]
    public void EventsValidator_MoreThan500ActivityEvents_Fails()
    {
        var result = EventsValidator().Validate(new IngestSessionEventsCommand
        {
            SessionId = "s-15",
            ActivityEvents = Enumerable.Range(0, 501)
                .Select(_ => new ActivityEventDto { State = ActivityState.Active, At = T0 }).ToList()
        });

        result.Errors.Should().Contain(e => e.PropertyName == nameof(IngestSessionEventsCommand.ActivityEvents));
    }

    [Fact]
    public void EventsValidator_MoreThan500ToolFailures_Fails()
    {
        var result = EventsValidator().Validate(new IngestSessionEventsCommand
        {
            SessionId = "s-16",
            ToolFailures = Enumerable.Range(0, 501)
                .Select(i => new ToolFailureDto { Id = $"f-{i}", ToolName = "Bash", At = T0 }).ToList()
        });

        result.Errors.Should().Contain(e => e.PropertyName == nameof(IngestSessionEventsCommand.ToolFailures));
    }

    [Fact]
    public void EventsValidator_Exactly500OfEach_Passes()
    {
        var result = EventsValidator().Validate(new IngestSessionEventsCommand
        {
            SessionId = "s-17",
            ActivityEvents = Enumerable.Range(0, 500)
                .Select(_ => new ActivityEventDto { State = ActivityState.Active, At = T0 }).ToList(),
            ToolFailures = Enumerable.Range(0, 500)
                .Select(i => new ToolFailureDto { Id = $"f-{i}", ToolName = "Bash", At = T0 }).ToList()
        });

        result.IsValid.Should().BeTrue();
    }

    // A laptop whose clock runs fast must not have every batch rejected: the Agent would retry a 400
    // forever. Future timestamps are accepted and clamped to now, so they cannot pin the state either.
    [Fact]
    public void EventsValidator_FarFutureTimestamps_Pass()
    {
        var farFuture = new DateTime(9999, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var result = EventsValidator().Validate(new IngestSessionEventsCommand
        {
            SessionId = "s-18",
            ActivityEvents = [new ActivityEventDto { State = ActivityState.Active, At = farFuture }],
            ToolFailures = [new ToolFailureDto { Id = "f-1", ToolName = "Bash", At = farFuture }]
        });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Events_FutureTimestamps_AreAppliedAndStoredAsNow()
    {
        var session = ExistingSession("s-19");
        session.ActivityState = ActivityState.Waiting;
        session.ActivityStateAt = T0.AddMinutes(-1);
        session.LastActivityAt = T0;

        await EventsHandler().Handle(new IngestSessionEventsCommand
        {
            SessionId = session.Id,
            ActivityEvents = [new ActivityEventDto { State = ActivityState.Active, At = Now.AddHours(3) }],
            ToolFailures = [new ToolFailureDto { Id = "f-future", ToolName = "Bash", At = Now.AddHours(3) }]
        }, CancellationToken.None);

        session.ActivityState.Should().Be(ActivityState.Active);
        session.ActivityStateAt.Should().Be(Now);
        session.LastToolErrorAt.Should().Be(Now);
        session.ToolErrorCount.Should().Be(1);
        await _repo.Received(1).AddToolFailuresAsync(
            Arg.Is<IEnumerable<SessionToolFailure>>(fs => fs.Single().At == Now), Arg.Any<CancellationToken>());
        session.LastActivityAt.Should().Be(Now);
    }

    // A batch carrying only tool failures still says when the session was last active.
    [Fact]
    public async Task Events_OnlyToolFailures_LastActivityIsTheLatestFailure()
    {
        var session = ExistingSession("s-20");
        session.LastActivityAt = T0.AddHours(-1);

        await EventsHandler().Handle(
            FailuresCommand(session.Id, ("f-1", "Bash", T0.AddMinutes(-30)), ("f-2", "Edit", T0.AddMinutes(-10))),
            CancellationToken.None);

        session.LastActivityAt.Should().Be(T0.AddMinutes(-10));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123x")]
    public async Task Events_ToolFailureIdEmptyOrOver64_IsRejectedAloneAndNotStored(string id)
    {
        var session = new Session { Id = "s-21" };
        _repo.GetByIdAsync("s-21", Arg.Any<CancellationToken>()).Returns(session);

        var result = await EventsHandler().Handle(
            FailuresCommand("s-21", (id, "Bash", T0), ("f-ok", "Bash", T0)), CancellationToken.None);

        // Per-item: the bad id is reported on its own and the valid failure is still stored.
        result.Value!.Results.Rejected.Should().ContainSingle().Which.Id.Should().Be(id);
        result.Value.Results.Accepted.Should().Equal("f-ok");
        _storedFailureIds.Should().Equal("f-ok");
        session.ToolErrorCount.Should().Be(1);
    }

    [Fact]
    public async Task Start_BackfillForEndedSession_KeepsItEndedAndFillsOnlyMissingMetadata()
    {
        var endedAt = T0.AddHours(-1);
        var lastActivity = T0.AddHours(-2);
        var existing = new Session
        {
            Id = "s-22", Status = SessionStatus.Completed, EndedAt = endedAt, LastActivityAt = lastActivity,
            Title = "kept-title", MachineName = null, ProjectKey = "github.com/o/kept"
        };
        _repo.GetByIdAsync("s-22", Arg.Any<CancellationToken>()).Returns(existing);

        await StartHandler().Handle(StartCommand("s-22") with
        {
            IsBackfill = true,
            Title = "backfill-title",
            MachineName = "box-a",
            ProjectKey = "github.com/o/other"
        }, CancellationToken.None);

        existing.Status.Should().Be(SessionStatus.Completed);
        existing.EndedAt.Should().Be(endedAt);
        existing.LastActivityAt.Should().Be(lastActivity);
        existing.Title.Should().Be("kept-title");
        existing.MachineName.Should().Be("box-a");
        existing.ProjectKey.Should().Be("github.com/o/kept");
    }

    [Fact]
    public async Task Start_BackfillForEndedSessionWithoutTitle_FillsTitle()
    {
        var existing = new Session { Id = "s-23", Status = SessionStatus.Completed, Title = null };
        _repo.GetByIdAsync("s-23", Arg.Any<CancellationToken>()).Returns(existing);

        await StartHandler().Handle(StartCommand("s-23") with { IsBackfill = true, Title = "from-backfill" },
            CancellationToken.None);

        existing.Title.Should().Be("from-backfill");
        existing.Status.Should().Be(SessionStatus.Completed);
    }

    [Fact]
    public async Task Start_BackfillForNewSession_CreatesActiveWithLastActivityAtStartedAt()
    {
        Session? stored = null;
        await _repo.AddAsync(Arg.Do<Session>(s => stored = s), Arg.Any<CancellationToken>());
        var startedAt = T0.AddDays(-30);

        await StartHandler().Handle(StartCommand("s-24") with { IsBackfill = true, StartedAt = startedAt },
            CancellationToken.None);

        stored!.Status.Should().Be(SessionStatus.Active);
        stored.StartedAt.Should().Be(startedAt);
        stored.LastActivityAt.Should().Be(startedAt);
    }

    private static IngestSessionEventsCommandValidator EventsValidator() => new();

    private static IngestSessionEventsCommand FailuresCommand(
        string sessionId, params (string Id, string ToolName, DateTime At)[] failures) => new()
    {
        SessionId = sessionId,
        ToolFailures = failures
            .Select(f => new ToolFailureDto { Id = f.Id, ToolName = f.ToolName, At = f.At })
            .ToList()
    };

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private IngestSessionStartCommandHandler StartHandler() => new(_repo, _uow);

    // The handler's clock sits after every fixture timestamp, so only deliberately future ones clamp.
    private static readonly DateTime Now = T0.AddHours(1);

    private IngestSessionEventsCommandHandler EventsHandler() => new(_repo, _uow, _pricing, _violations, new FixedTime(Now));

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
