namespace DeveloperOverwatch.Application.Tests.Behaviors;

using DeveloperOverwatch.Application.Behaviors;
using Xunit;
using DeveloperOverwatch.Domain.Common;
using FluentAssertions;
using FluentValidation;
using MediatR;
using NSubstitute;

public record TestCommand(string Name) : IRequest<Result<string>>;

public class TestCommandValidator : AbstractValidator<TestCommand>
{
    public TestCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
    }
}

public class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_ValidRequest_CallsNext()
    {
        var validator = new TestCommandValidator();
        var behavior = new ValidationBehavior<TestCommand, Result<string>>(
            [validator],
            Substitute.For<Microsoft.Extensions.Logging.ILogger<ValidationBehavior<TestCommand, Result<string>>>>());

        var next = Substitute.For<RequestHandlerDelegate<Result<string>>>();
        next().Returns(Result<string>.Success("ok"));

        var result = await behavior.Handle(new TestCommand("valid"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await next.Received(1)();
    }

    [Fact]
    public async Task Handle_InvalidRequest_ReturnsValidationFailure()
    {
        var validator = new TestCommandValidator();
        var behavior = new ValidationBehavior<TestCommand, Result<string>>(
            [validator],
            Substitute.For<Microsoft.Extensions.Logging.ILogger<ValidationBehavior<TestCommand, Result<string>>>>());

        var next = Substitute.For<RequestHandlerDelegate<Result<string>>>();

        var result = await behavior.Handle(new TestCommand(""), next, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ValidationErrors.Should().Contain(e => e.Contains("Name is required"));
        await next.DidNotReceive()();
    }
}
