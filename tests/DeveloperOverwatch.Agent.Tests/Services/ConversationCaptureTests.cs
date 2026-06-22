using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DeveloperOverwatch.Agent.Tests.Services;

// ConversationCapture resolves against the user's real ~/.claude/projects directory
// (a hardcoded static path), so CaptureAsync parsing cannot be exercised deterministically.
// FindProjectDir's enumeration + matching logic is, however, deterministic for a repo path
// that cannot match any real project directory (a random GUID under a temp root).
public sealed class ConversationCaptureTests
{
    private readonly ConversationCapture _sut =
        new(Substitute.For<ILogger<ConversationCapture>>());

    [Fact]
    public void FindProjectDir_PathThatMatchesNoProjectDir_ReturnsNull()
    {
        var unmatchablePath = Path.Combine(
            Path.GetTempPath(),
            $"overwatch-no-match-{Guid.NewGuid():N}",
            $"sub-{Guid.NewGuid():N}");

        var result = _sut.FindProjectDir(unmatchablePath);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CaptureAsync_PathThatMatchesNoProjectDir_DoesNotThrow()
    {
        var unmatchablePath = Path.Combine(
            Path.GetTempPath(),
            $"overwatch-no-match-{Guid.NewGuid():N}");

        var act = async () => await _sut.CaptureAsync(unmatchablePath, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
