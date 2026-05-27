using System.Text.Json;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace DeveloperOverwatch.Agent.Tests.Services;

public sealed class GitFallbackWriterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly GitFallbackWriter _sut;

    public GitFallbackWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"overwatch-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var logger = Substitute.For<ILogger<GitFallbackWriter>>();
        _sut = new GitFallbackWriter(logger);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static QueuedEvent CreateEvent(
        string sessionId = "test-session",
        EventType type = EventType.ToolExecution,
        string payload = "{\"tool\":\"read\"}") => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        SessionId = sessionId,
        EventType = type,
        PayloadJson = payload,
        CreatedAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public async Task WriteFallback_ValidPath_CreatesJsonFile()
    {
        var sessionId = "abc-123";
        var events = new List<QueuedEvent> { CreateEvent(sessionId) };

        var result = await _sut.WriteFallbackAsync(sessionId, events, _tempDir, CancellationToken.None);

        result.Should().BeTrue();
        var expectedPath = Path.Combine(_tempDir, ".sessions", $"{sessionId}.json");
        File.Exists(expectedPath).Should().BeTrue("the fallback file should be created in .sessions/");
    }

    [Fact]
    public async Task WriteFallback_NullRepoPath_ReturnsFalse()
    {
        var events = new List<QueuedEvent> { CreateEvent() };

        var result = await _sut.WriteFallbackAsync("session-1", events, null, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WriteFallback_EmptyRepoPath_ReturnsFalse(string repoPath)
    {
        var events = new List<QueuedEvent> { CreateEvent() };

        var result = await _sut.WriteFallbackAsync("session-1", events, repoPath, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("../malicious")]
    [InlineData("..\\malicious")]
    [InlineData("hello world")]
    [InlineData("session;drop")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public async Task WriteFallback_UnsafeSessionId_ReturnsFalse(string sessionId)
    {
        var events = new List<QueuedEvent> { CreateEvent(sessionId) };

        var result = await _sut.WriteFallbackAsync(sessionId, events, _tempDir, CancellationToken.None);

        result.Should().BeFalse();
        var sessionsDir = Path.Combine(_tempDir, ".sessions");
        if (Directory.Exists(sessionsDir))
            Directory.GetFiles(sessionsDir).Should().BeEmpty("no file should be written for unsafe session IDs");
    }

    [Fact]
    public async Task WriteFallback_NonexistentRepo_ReturnsFalse()
    {
        var fakePath = Path.Combine(_tempDir, "does-not-exist");
        var events = new List<QueuedEvent> { CreateEvent() };

        var result = await _sut.WriteFallbackAsync("session-1", events, fakePath, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task WriteFallback_WritesCorrectJson()
    {
        var sessionId = "session-42";
        var createdAt = new DateTime(2026, 5, 27, 14, 30, 0, DateTimeKind.Utc);
        var events = new List<QueuedEvent>
        {
            new()
            {
                Id = "evt-001",
                SessionId = sessionId,
                EventType = EventType.SessionStart,
                PayloadJson = "{\"start\":true}",
                CreatedAt = createdAt
            },
            new()
            {
                Id = "evt-002",
                SessionId = sessionId,
                EventType = EventType.FileChange,
                PayloadJson = "{\"file\":\"Program.cs\"}",
                CreatedAt = createdAt.AddMinutes(5)
            }
        };

        await _sut.WriteFallbackAsync(sessionId, events, _tempDir, CancellationToken.None);

        var filePath = Path.Combine(_tempDir, ".sessions", $"{sessionId}.json");
        var json = await File.ReadAllTextAsync(filePath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("sessionId").GetString().Should().Be(sessionId);

        var jsonEvents = root.GetProperty("events");
        jsonEvents.GetArrayLength().Should().Be(2);

        var first = jsonEvents[0];
        first.GetProperty("id").GetString().Should().Be("evt-001");
        first.GetProperty("eventType").GetString().Should().Be("SessionStart");
        first.GetProperty("payloadJson").GetString().Should().Be("{\"start\":true}");
        first.GetProperty("createdAt").GetDateTime().Should().Be(createdAt);

        var second = jsonEvents[1];
        second.GetProperty("id").GetString().Should().Be("evt-002");
        second.GetProperty("eventType").GetString().Should().Be("FileChange");
        second.GetProperty("payloadJson").GetString().Should().Be("{\"file\":\"Program.cs\"}");
    }

    [Fact]
    public async Task WriteFallback_MultipleCallsSameSession_OverwritesFile()
    {
        var sessionId = "overwrite-test";
        var firstEvents = new List<QueuedEvent> { CreateEvent(sessionId, payload: "{\"v\":1}") };
        var secondEvents = new List<QueuedEvent> { CreateEvent(sessionId, payload: "{\"v\":2}") };

        await _sut.WriteFallbackAsync(sessionId, firstEvents, _tempDir, CancellationToken.None);
        await _sut.WriteFallbackAsync(sessionId, secondEvents, _tempDir, CancellationToken.None);

        var filePath = Path.Combine(_tempDir, ".sessions", $"{sessionId}.json");
        var json = await File.ReadAllTextAsync(filePath);
        using var doc = JsonDocument.Parse(json);
        var payloadJson = doc.RootElement.GetProperty("events")[0].GetProperty("payloadJson").GetString();
        payloadJson.Should().Contain("\"v\":2");
    }

    [Fact]
    public async Task WriteFallback_EmptyEventsList_WritesEmptyArray()
    {
        var sessionId = "empty-events";
        var events = new List<QueuedEvent>();

        var result = await _sut.WriteFallbackAsync(sessionId, events, _tempDir, CancellationToken.None);

        result.Should().BeTrue();
        var filePath = Path.Combine(_tempDir, ".sessions", $"{sessionId}.json");
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));
        doc.RootElement.GetProperty("events").GetArrayLength().Should().Be(0);
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("with-dashes")]
    [InlineData("with_underscores")]
    [InlineData("ABC123")]
    [InlineData("MixedCase-With_All")]
    public async Task WriteFallback_SafeSessionIds_Accepted(string sessionId)
    {
        var events = new List<QueuedEvent> { CreateEvent(sessionId) };

        var result = await _sut.WriteFallbackAsync(sessionId, events, _tempDir, CancellationToken.None);

        result.Should().BeTrue();
    }
}
