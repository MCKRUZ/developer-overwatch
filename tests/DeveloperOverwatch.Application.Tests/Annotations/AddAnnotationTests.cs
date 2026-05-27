namespace DeveloperOverwatch.Application.Tests.Annotations;

using DeveloperOverwatch.Application.Commands.AddAnnotation;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;
using Xunit;

public class AddAnnotationTests
{
    private readonly ISessionRepository _sessionRepo = Substitute.For<ISessionRepository>();
    private readonly IArchitectAnnotationRepository _annotationRepo = Substitute.For<IArchitectAnnotationRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly AddAnnotationCommandHandler _handler;

    public AddAnnotationTests()
    {
        _handler = new AddAnnotationCommandHandler(_sessionRepo, _annotationRepo, _unitOfWork);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesAnnotation()
    {
        var session = new Session { Id = "s1", DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow };
        _sessionRepo.GetByIdAsync("s1", Arg.Any<CancellationToken>()).Returns(session);

        var command = new AddAnnotationCommand("s1", "Good work here", "architect@test.com");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNullOrEmpty();
        await _annotationRepo.Received(1).AddAsync(
            Arg.Is<ArchitectAnnotation>(a =>
                a.SessionId == "s1" &&
                a.Content == "Good work here" &&
                a.CreatedBy == "architect@test.com"),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SessionNotFound_ReturnsFail()
    {
        _sessionRepo.GetByIdAsync("nonexistent", Arg.Any<CancellationToken>()).Returns((Session?)null);

        var command = new AddAnnotationCommand("nonexistent", "Note", "architect@test.com");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Handle_WithTurnId_SetsOptionalFields()
    {
        var session = new Session { Id = "s1", DeveloperEmail = "dev@test.com", StartedAt = DateTime.UtcNow };
        _sessionRepo.GetByIdAsync("s1", Arg.Any<CancellationToken>()).Returns(session);

        var command = new AddAnnotationCommand("s1", "Note on turn", "architect@test.com", TurnId: "t1", ToolExecutionId: "te1");
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _annotationRepo.Received(1).AddAsync(
            Arg.Is<ArchitectAnnotation>(a => a.TurnId == "t1" && a.ToolExecutionId == "te1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validator_EmptySessionId_Fails()
    {
        var validator = new AddAnnotationCommandValidator();
        var command = new AddAnnotationCommand("", "Content", "architect@test.com");
        var result = await validator.ValidateAsync(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "SessionId");
    }

    [Fact]
    public async Task Validator_EmptyContent_Fails()
    {
        var validator = new AddAnnotationCommandValidator();
        var command = new AddAnnotationCommand("s1", "", "architect@test.com");
        var result = await validator.ValidateAsync(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Content");
    }

    [Fact]
    public async Task Validator_InvalidEmail_Fails()
    {
        var validator = new AddAnnotationCommandValidator();
        var command = new AddAnnotationCommand("s1", "Content", "not-an-email");
        var result = await validator.ValidateAsync(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CreatedBy");
    }
}
