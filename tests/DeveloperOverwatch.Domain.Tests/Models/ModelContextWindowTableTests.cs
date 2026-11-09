namespace DeveloperOverwatch.Domain.Tests.Models;

using DeveloperOverwatch.Domain.Models;
using FluentAssertions;
using Xunit;

public class ModelContextWindowTableTests
{
    private static readonly DateOnly AsOf = new(2026, 10, 6);

    private static ModelContextWindowTable CreateSut() => new(AsOf, new Dictionary<string, long>
    {
        ["default"] = 1_000_000,
        ["claude-haiku-4-5"] = 200_000,
    });

    [Fact]
    public void AsOf_ReturnsTheTableDate()
    {
        CreateSut().AsOf.Should().Be(AsOf);
    }

    [Theory]
    [InlineData("claude-haiku-4-5", 200_000L)]
    [InlineData("claude-haiku-4-5-20251001", 200_000L)]
    [InlineData("claude-opus-5-5", 1_000_000L)]
    [InlineData(null, 1_000_000L)]
    public void ContextWindowTokensFor_ResolvesByLongestPrefixThenDefault(string? model, long expected)
    {
        CreateSut().ContextWindowTokensFor(model).Should().Be(expected);
    }
}
