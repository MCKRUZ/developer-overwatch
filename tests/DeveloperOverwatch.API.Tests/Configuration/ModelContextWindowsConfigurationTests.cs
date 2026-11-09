namespace DeveloperOverwatch.API.Tests.Configuration;

using DeveloperOverwatch.Domain.Models;
using DeveloperOverwatch.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

public class ModelContextWindowsConfigurationTests
{
    private static ModelContextWindowTable Bind(IConfiguration configuration) =>
        configuration.GetSection("ModelContextWindows").Get<ModelContextWindowsOptions>()!.ToTable();

    [Theory]
    [InlineData("claude-haiku-4-5", 200_000L)]
    [InlineData("claude-haiku-4-5-20251001", 200_000L)]
    [InlineData("claude-haiku-5-5", 1_000_000L)]
    [InlineData("claude-opus-5-5", 1_000_000L)]
    [InlineData("claude-opus-5-20260101", 1_000_000L)]
    [InlineData("claude-opus-4-6", 1_000_000L)]
    [InlineData("claude-sonnet-4-6", 1_000_000L)]
    [InlineData("claude-opus-4-5", 200_000L)]
    [InlineData("claude-opus-4-5-20251101", 200_000L)]
    [InlineData("claude-sonnet-4-5", 200_000L)]
    [InlineData("claude-sonnet-4-5-20250929", 200_000L)]
    [InlineData("claude-opus-4-1", 200_000L)]
    [InlineData("claude-opus-4-1-20250805", 200_000L)]
    [InlineData("claude-opus-4", 200_000L)]
    [InlineData("claude-opus-4-0", 200_000L)]
    [InlineData("claude-opus-4-20250514", 200_000L)]
    [InlineData("claude-sonnet-4", 200_000L)]
    [InlineData("claude-sonnet-4-0", 200_000L)]
    [InlineData("claude-sonnet-4-20250514", 200_000L)]
    [InlineData("claude-fable-5-1", 1_000_000L)]
    [InlineData("an-unknown-model", 1_000_000L)]
    public void RealAppSettings_ContextWindows_MatchPublishedSizes(string model, long expected)
    {
        Bind(RepoAppSettings.Load()).ContextWindowTokensFor(model).Should().Be(expected);
    }

    [Fact]
    public void RealAppSettings_AsOf_IsTheModelListDate()
    {
        Bind(RepoAppSettings.Load()).AsOf.Should().Be(new DateOnly(2026, 11, 9));
    }

    [Fact]
    public void Bind_MissingAsOf_Throws()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ModelContextWindows:Models:default"] = "1000" })
            .Build();

        var act = () => Bind(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*AsOf*");
    }
}
