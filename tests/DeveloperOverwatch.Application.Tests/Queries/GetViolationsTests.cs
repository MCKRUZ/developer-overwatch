namespace DeveloperOverwatch.Application.Tests.Queries;

using DeveloperOverwatch.Application.Queries.GetViolations;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class GetViolationsTests
{
    private readonly IGovernanceViolationRepository _repo = Substitute.For<IGovernanceViolationRepository>();

    [Fact]
    public async Task Handle_ReturnsViolationsForSession()
    {
        var violations = new List<GovernanceViolation>
        {
            new() { Id = "v1", SessionId = "s1", RuleId = "r1", Severity = ViolationSeverity.Error, Message = "test" }
        };
        _repo.GetBySessionIdAsync("s1", (ViolationSeverity?)null, Arg.Any<CancellationToken>())
            .Returns(violations);

        var handler = new GetViolationsQueryHandler(_repo);
        var result = await handler.Handle(
            new GetViolationsQuery { SessionId = "s1" }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_WithSeverityFilter_FiltersCorrectly()
    {
        var violations = new List<GovernanceViolation>
        {
            new() { Id = "v2", SessionId = "s1", RuleId = "r1", Severity = ViolationSeverity.Critical, Message = "critical" }
        };
        _repo.GetBySessionIdAsync("s1", ViolationSeverity.Critical, Arg.Any<CancellationToken>())
            .Returns(violations);

        var handler = new GetViolationsQueryHandler(_repo);
        var result = await handler.Handle(
            new GetViolationsQuery { SessionId = "s1", Severity = ViolationSeverity.Critical },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Severity.Should().Be(ViolationSeverity.Critical);
    }

    [Fact]
    public async Task Handle_NoViolations_ReturnsEmptyList()
    {
        _repo.GetBySessionIdAsync("s1", (ViolationSeverity?)null, Arg.Any<CancellationToken>())
            .Returns(new List<GovernanceViolation>());

        var handler = new GetViolationsQueryHandler(_repo);
        var result = await handler.Handle(
            new GetViolationsQuery { SessionId = "s1" }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
