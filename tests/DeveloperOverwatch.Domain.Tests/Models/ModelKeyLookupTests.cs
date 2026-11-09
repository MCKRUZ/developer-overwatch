namespace DeveloperOverwatch.Domain.Tests.Models;

using DeveloperOverwatch.Domain.Models;
using FluentAssertions;
using Xunit;

public class ModelKeyLookupTests
{
    private static ModelKeyLookup<string> CreateSut() => new(new Dictionary<string, string>
    {
        ["default"] = "default",
        ["claude-opus"] = "opus-family",
        ["claude-opus-4"] = "opus-4",
        ["claude-opus-4-1"] = "opus-4-1",
        ["claude-opus-5"] = "opus-5",
        ["claude-opus-5-5"] = "opus-5-5",
        ["claude-fable-5"] = "fable-5",
        ["claude-fable-5-1"] = "fable-5-1",
        ["claude-sonnet-4-5"] = "sonnet-4-5",
    });

    [Theory]
    [InlineData("claude-opus-5-5", "opus-5-5")]
    [InlineData("CLAUDE-OPUS-5-5", "opus-5-5")]
    [InlineData("claude-opus-5-20260101", "opus-5")]
    [InlineData("claude-fable-5-1", "fable-5-1")]
    [InlineData("claude-fable-5-20260301", "fable-5")]
    [InlineData("claude-sonnet-4-5-20250929", "sonnet-4-5")]
    [InlineData("claude-opus-4-20250514", "opus-4")]
    [InlineData("claude-opus-4-1-20250805", "opus-4-1")]
    [InlineData("claude-opus-7", "opus-family")]
    public void Resolve_ModelId_PicksExactThenLongestPrefix(string model, string expected)
    {
        CreateSut().Resolve(model).Should().Be(expected);
    }

    [Fact]
    public void Resolve_KeyEndsMidVersionNumber_DoesNotMatch()
    {
        // "claude-opus-4-1" is a string prefix of "claude-opus-4-10" but names a different version.
        CreateSut().Resolve("claude-opus-4-10").Should().Be("opus-4");
    }

    [Theory]
    [InlineData("claude-opus-5-5@20260101")]
    [InlineData("claude-opus-5-5[1m]")]
    public void Resolve_NonAlphanumericSuffix_CountsAsVersionBoundary(string model)
    {
        CreateSut().Resolve(model).Should().Be("opus-5-5");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gpt-5")]
    [InlineData("claude")]
    public void Resolve_NoMatch_ReturnsDefault(string? model)
    {
        CreateSut().Resolve(model).Should().Be("default");
    }

    [Fact]
    public void Resolve_DefaultKeyIsNeverUsedAsAPrefix()
    {
        CreateSut().Resolve("default-ish-model").Should().Be("default");
    }

    [Fact]
    public void Constructor_NoDefaultEntry_Throws()
    {
        var act = () => new ModelKeyLookup<string>(new Dictionary<string, string> { ["claude-opus"] = "x" });

        act.Should().Throw<ArgumentException>().WithMessage("*default*");
    }

    [Fact]
    public void Constructor_KeysDifferingOnlyByCase_Throws()
    {
        var entries = new[]
        {
            KeyValuePair.Create("default", "d"),
            KeyValuePair.Create("claude-opus", "a"),
            KeyValuePair.Create("Claude-Opus", "b"),
        };

        var act = () => new ModelKeyLookup<string>(entries);

        act.Should().Throw<ArgumentException>();
    }
}
