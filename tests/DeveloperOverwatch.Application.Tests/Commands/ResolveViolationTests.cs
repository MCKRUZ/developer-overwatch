namespace DeveloperOverwatch.Application.Tests.Commands;

using DeveloperOverwatch.Application.Commands.ResolveViolation;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class ResolveViolationTests
{
    private readonly IGovernanceViolationRepository _repo = Substitute.For<IGovernanceViolationRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task Handle_ExistingViolation_ResolvesIt()
    {
        var violation = new GovernanceViolation
        {
            Id = "v1", SessionId = "s1", RuleId = "r1",
            Severity = ViolationSeverity.Error, Message = "test"
        };
        _repo.GetByIdAsync("v1", Arg.Any<CancellationToken>()).Returns(violation);

        var handler = new ResolveViolationCommandHandler(_repo, _uow);
        var command = new ResolveViolationCommand
        {
            ViolationId = "v1",
            ResolvedBy = "architect@test.com",
            ResolutionReason = "false positive"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        violation.Resolved.Should().BeTrue();
        violation.ResolvedBy.Should().Be("architect@test.com");
        violation.ResolutionReason.Should().Be("false positive");
        violation.ResolvedAt.Should().NotBeNull();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ViolationNotFound_ReturnsFail()
    {
        _repo.GetByIdAsync("nonexistent", Arg.Any<CancellationToken>()).Returns((GovernanceViolation?)null);

        var handler = new ResolveViolationCommandHandler(_repo, _uow);
        var command = new ResolveViolationCommand
        {
            ViolationId = "nonexistent",
            ResolvedBy = "architect@test.com",
            ResolutionReason = "n/a"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_AlreadyResolved_ReturnsFail()
    {
        var violation = new GovernanceViolation
        {
            Id = "v2", SessionId = "s1", RuleId = "r1",
            Severity = ViolationSeverity.Error, Message = "test",
            Resolved = true, ResolvedBy = "someone@test.com"
        };
        _repo.GetByIdAsync("v2", Arg.Any<CancellationToken>()).Returns(violation);

        var handler = new ResolveViolationCommandHandler(_repo, _uow);
        var command = new ResolveViolationCommand
        {
            ViolationId = "v2",
            ResolvedBy = "architect@test.com",
            ResolutionReason = "duplicate"
        };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already resolved");
    }

    [Fact]
    public void Validator_MissingFields_ReturnsErrors()
    {
        var validator = new ResolveViolationCommandValidator();
        var command = new ResolveViolationCommand
        {
            ViolationId = "",
            ResolvedBy = "",
            ResolutionReason = ""
        };

        var result = validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThanOrEqualTo(3);
    }
}
