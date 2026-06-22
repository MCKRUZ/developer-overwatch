using System.Diagnostics;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DeveloperOverwatch.Agent.Tests.Services;

// Exercises AddGitTrailerAsync against a real temporary git repository.
// git is a hard dependency of this code path, so these are integration-style tests.
public sealed class GitFallbackWriterTrailerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly GitFallbackWriter _sut;

    public GitFallbackWriterTrailerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"overwatch-git-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _sut = new GitFallbackWriter(Substitute.For<ILogger<GitFallbackWriter>>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            // Git marks pack files read-only on Windows; clear attributes before delete.
            foreach (var file in Directory.EnumerateFiles(_tempDir, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static void Git(string repoPath, string args)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"-C \"{repoPath}\" {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        process.WaitForExit();
        process.ExitCode.Should().Be(0, $"git {args} should succeed: {process.StandardError.ReadToEnd()}");
    }

    private void InitRepoWithCommit()
    {
        Git(_tempDir, "init");
        Git(_tempDir, "config user.email test@test.com");
        Git(_tempDir, "config user.name Tester");
        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "hello");
        Git(_tempDir, "add a.txt");
        Git(_tempDir, "commit -m initial");
    }

    [Fact]
    public async Task AddGitTrailer_RepoWithCommit_AddsNoteAndReturnsTrue()
    {
        InitRepoWithCommit();

        var result = await _sut.AddGitTrailerAsync("session-123", _tempDir, CancellationToken.None);

        result.Should().BeTrue();

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"-C \"{_tempDir}\" notes show HEAD",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        var noteBody = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        noteBody.Should().Contain("Claude-Session: session-123");
    }

    [Fact]
    public async Task AddGitTrailer_RepoWithNoCommits_ReturnsFalse()
    {
        Git(_tempDir, "init");
        Git(_tempDir, "config user.email test@test.com");
        Git(_tempDir, "config user.name Tester");

        var result = await _sut.AddGitTrailerAsync("session-123", _tempDir, CancellationToken.None);

        result.Should().BeFalse("there is no HEAD commit to attach a note to");
    }

    [Fact]
    public async Task AddGitTrailer_NullRepoPath_ReturnsFalse()
    {
        var result = await _sut.AddGitTrailerAsync("session-123", null!, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task AddGitTrailer_NonexistentRepoPath_ReturnsFalse()
    {
        var fake = Path.Combine(_tempDir, "does-not-exist");

        var result = await _sut.AddGitTrailerAsync("session-123", fake, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("with space")]
    [InlineData("semi;colon")]
    public async Task AddGitTrailer_UnsafeSessionId_ReturnsFalse(string sessionId)
    {
        InitRepoWithCommit();

        var result = await _sut.AddGitTrailerAsync(sessionId, _tempDir, CancellationToken.None);

        result.Should().BeFalse();
    }
}
