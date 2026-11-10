namespace DeveloperOverwatch.Application.Tests.Behaviors;

using DeveloperOverwatch.Application.Behaviors;
using DeveloperOverwatch.Application.Commands;
using DeveloperOverwatch.Application.Commands.IngestSessionEnd;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

public class GovernanceBehaviorTests
{
    private readonly IGovernanceEngine _engine = Substitute.For<IGovernanceEngine>();
    private readonly IOverwatchNotifier _notifier = Substitute.For<IOverwatchNotifier>();
    private readonly GovernanceBehavior _behavior;
    private readonly IngestSessionEndCommand _command = new() { SessionId = "s1", EndedAt = DateTime.UtcNow };

    public GovernanceBehaviorTests()
    {
        _behavior = new GovernanceBehavior(_engine, _notifier, NullLogger<GovernanceBehavior>.Instance);
    }

    [Fact]
    public async Task Handle_EndThatClosedTheSession_TriggersGovernance()
    {
        _engine.EvaluateAsync("s1", Arg.Any<CancellationToken>())
            .Returns(new List<GovernanceViolation>
            {
                new() { Id = "v1", SessionId = "s1", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "test" }
            });

        var result = await _behavior.Handle(_command, Next(IngestOutcome.Accepted, closed: true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _engine.Received(1).EvaluateAsync("s1", Arg.Any<CancellationToken>());
        await _notifier.Received(1).ViolationCreatedAsync(
            Arg.Is<GovernanceViolation>(v => v.Id == "v1"), Arg.Any<CancellationToken>());
    }

    // A redelivered end (duplicate event id) and an end for a session already over both succeed,
    // but neither closed anything, so neither may raise the session's violations again.
    [Theory]
    [InlineData(IngestOutcome.Duplicate)]
    [InlineData(IngestOutcome.Accepted)]
    public async Task Handle_EndThatClosedNothing_SkipsGovernance(IngestOutcome outcome)
    {
        var result = await _behavior.Handle(_command, Next(outcome, closed: false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _engine.DidNotReceive().EvaluateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FailedSessionEnd_SkipsGovernance()
    {
        RequestHandlerDelegate<Result<IngestSessionEndResult>> next = _ =>
            Task.FromResult(Result<IngestSessionEndResult>.Fail("Session not found"));

        var result = await _behavior.Handle(_command, next, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        await _engine.DidNotReceive().EvaluateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // The session closed and its end will be answered duplicate if redelivered, so governance cut short by
    // a client that gave up waiting would never run.
    [Fact]
    public async Task Handle_RequestCancelledAfterTheClose_StillEvaluatesAndNotifies()
    {
        _engine.EvaluateAsync("s1", Arg.Any<CancellationToken>()).Returns(ci =>
        {
            ci.Arg<CancellationToken>().ThrowIfCancellationRequested();
            return new List<GovernanceViolation>
            {
                new() { Id = "v1", SessionId = "s1", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "test" }
            };
        });
        _notifier.ViolationCreatedAsync(Arg.Any<GovernanceViolation>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<CancellationToken>().IsCancellationRequested
                ? Task.FromCanceled(ci.Arg<CancellationToken>())
                : Task.CompletedTask);
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();

        var result = await _behavior.Handle(_command, Next(IngestOutcome.Accepted, closed: true), aborted.Token);

        result.IsSuccess.Should().BeTrue();
        await _notifier.Received(1).ViolationCreatedAsync(
            Arg.Is<GovernanceViolation>(v => v.Id == "v1"), Arg.Is<CancellationToken>(t => !t.IsCancellationRequested));
    }

    [Fact]
    public async Task Handle_GovernanceThrows_DoesNotFailSessionEnd()
    {
        _engine.EvaluateAsync("s1", Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Policy loading failed"));

        var result = await _behavior.Handle(_command, Next(IngestOutcome.Accepted, closed: true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    private static RequestHandlerDelegate<Result<IngestSessionEndResult>> Next(IngestOutcome outcome, bool closed) =>
        _ => Task.FromResult(Result<IngestSessionEndResult>.Success(new IngestSessionEndResult(outcome, closed)));
}
