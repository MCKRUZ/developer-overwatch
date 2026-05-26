namespace DeveloperOverwatch.Domain.Tests.Common;

using DeveloperOverwatch.Domain.Common;
using FluentAssertions;
using Xunit;

public class ResultTests
{
    [Fact]
    public void Success_ShouldSetIsSuccessTrue()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Fail_ShouldSetIsSuccessFalse()
    {
        var result = Result<int>.Fail("something went wrong");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("something went wrong");
    }

    [Fact]
    public void ValidationFailure_ShouldContainAllErrors()
    {
        var errors = new List<string> { "field1 required", "field2 invalid" };

        var result = Result<int>.ValidationFailure(errors);

        result.IsSuccess.Should().BeFalse();
        result.ValidationErrors.Should().BeEquivalentTo(errors);
    }
}
