using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Xunit;

namespace DeveloperOverwatch.Agent.Tests.Services;

// The project key groups sessions across clones and machines, and it leaves the machine — so every
// spelling of the same remote must collapse to one key, and no credential may survive.
public sealed class ProjectKeyTests
{
    [Theory]
    [InlineData("https://github.com/acme/widget.git", "github.com/acme/widget")]
    [InlineData("https://github.com/acme/widget", "github.com/acme/widget")]
    [InlineData("https://github.com/acme/widget/", "github.com/acme/widget")]
    [InlineData("https://github.com/acme/widget.git/", "github.com/acme/widget")]
    [InlineData("https://user:token@github.com/acme/widget", "github.com/acme/widget")]
    [InlineData("https://ghp_abc123@github.com/acme/widget.git", "github.com/acme/widget")]
    [InlineData("HTTPS://GitHub.COM/acme/widget.git", "github.com/acme/widget")]
    [InlineData("https://github.com/acme/widget.git?ref=main#readme", "github.com/acme/widget")]
    [InlineData("git@github.com:acme/widget.git", "github.com/acme/widget")]
    [InlineData("git@github.com:acme/widget", "github.com/acme/widget")]
    [InlineData("ssh://git@github.com:22/acme/widget.git", "github.com/acme/widget")]
    [InlineData("ssh://git@github.com/acme/widget.git", "github.com/acme/widget")]
    [InlineData("git+ssh://git@github.com/acme/widget.git", "github.com/acme/widget")]
    [InlineData("git+ssh://user:token@github.com:2222/acme/widget.git", "github.com/acme/widget")]
    [InlineData("https://org@dev.azure.com/org/proj/_git/repo", "dev.azure.com/org/proj/_git/repo")]
    [InlineData("  https://github.com/acme/widget.git\n", "github.com/acme/widget")]
    public void From_RemoteForms_NormalizeToHostOwnerRepo(string remote, string expected)
    {
        var key = ProjectKey.From(remote, "C:/Code Repos/ignored");

        key.Should().Be(expected);
        key.Should().NotContain("@").And.NotContain("token").And.NotContain("user:").And.NotContain("ghp_");
    }

    [Theory]
    [InlineData(null, "C:/Code Repos/widget", "widget")]
    [InlineData("", "C:\\Code Repos\\widget\\", "widget")]
    [InlineData("C:/local/bare/widget.git", "/home/dev/widget/", "widget")]
    [InlineData("/srv/git/widget.git", "/home/dev/widget", "widget")]
    [InlineData("file:///srv/git/widget.git", "/home/dev/widget", "widget")]
    public void From_NoUsableRemote_FallsBackToFolderName(string? remote, string repoPath, string expected) =>
        ProjectKey.From(remote, repoPath).Should().Be(expected);

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "/")]
    public void From_NoRemoteAndNoFolder_ReturnsNull(string? remote, string? repoPath) =>
        ProjectKey.From(remote, repoPath).Should().BeNull();
}
