namespace DeveloperOverwatch.Application.Tests.Behaviors;

using DeveloperOverwatch.Application.Behaviors;
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

    public GovernanceBehaviorTests()
    {
        _behavior = new GovernanceBehavior(_engine, _notifier, NullLogger<GovernanceBehavior>.Instance);
    }

    [Fact]
    public async Task Handle_SuccessfulSessionEnd_TriggersGovernance()
    {
        var command = new IngestSessionEndCommand { SessionId = "s1", EndedAt = DateTime.UtcNow };
        _engine.EvaluateAsync("s1", Arg.Any<CancellationToken>())
            .Returns(new List<GovernanceViolation>
            {
                new() { Id = "v1", SessionId = "s1", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "test" }
            });

        RequestHandlerDelegate<Result<string>> next = _ =>
            Task.FromResult(Result<string>.Success("s1"));

        var result = await _behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _engine.Received(1).EvaluateAsync("s1", Arg.Any<CancellationToken>());
        await _notifier.Received(1).ViolationCreatedAsync(
            Arg.Is<GovernanceViolation>(v => v.Id == "v1"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FailedSessionEnd_SkipsGovernance()
    {
        var command = new IngestSessionEndCommand { SessionId = "s1", EndedAt = DateTime.UtcNow };

        RequestHandlerDelegate<Result<string>> next = _ =>
            Task.FromResult(Result<string>.Fail("Session not found"));

        var result = await _behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        await _engine.DidNotReceive().EvaluateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_GovernanceThrows_DoesNotFailSessionEnd()
    {
        var command = new IngestSessionEndCommand { SessionId = "s1", EndedAt = DateTime.UtcNow };
        _engine.EvaluateAsync("s1", Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Policy loading failed"));

        RequestHandlerDelegate<Result<string>> next = _ =>
            Task.FromResult(Result<string>.Success("s1"));

        var result = await _behavior.Handle(command, next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
