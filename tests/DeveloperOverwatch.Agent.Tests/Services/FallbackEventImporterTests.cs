using System.Text.Json;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace DeveloperOverwatch.Agent.Tests.Services;

public sealed class FallbackEventImporterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-import-{Guid.NewGuid():N}");
    private readonly IEventQueue _queue = Substitute.For<IEventQueue>();
    private readonly ILogger _logger = Substitute.For<ILogger>();
    private readonly List<string> _enqueuedIds = [];

    public FallbackEventImporterTests()
    {
        Directory.CreateDirectory(_dir);
        _queue.EnqueueAsync(Arg.Do<QueuedEvent>(e => _enqueuedIds.Add(e.Id)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string FallbackPath => Path.Combine(_dir, "queue-fallback.jsonl");

    private static string Line(string id) => JsonSerializer.Serialize(new
    {
        id,
        sessionId = "ow-1",
        eventType = "ToolFailed",
        payload = "{\"toolName\":\"Bash\",\"at\":\"2026-11-09T10:00:00.0000000Z\"}",
        createdAt = "2026-11-09T10:00:00.0000000Z"
    });

    [Fact]
    public async Task ImportAsync_HookAppendsDuringImport_LineSurvivesToNextPoll()
    {
        // The old importer read the file, then deleted it: a hook appending in between lost its event.
        await File.WriteAllTextAsync(FallbackPath, Line("first") + Environment.NewLine);
        var appended = false;
        _queue.When(q => q.EnqueueAsync(Arg.Any<QueuedEvent>(), Arg.Any<CancellationToken>())).Do(_ =>
        {
            if (appended) return;
            appended = true;
            File.AppendAllText(FallbackPath, Line("during-import") + Environment.NewLine);
        });

        await FallbackEventImporter.ImportAsync(_dir, _queue, _logger);

        _enqueuedIds.Should().Equal("first");
        File.Exists(FallbackPath).Should().BeTrue("the hook's late line must not be deleted with the claimed file");

        await FallbackEventImporter.ImportAsync(_dir, _queue, _logger);

        _enqueuedIds.Should().Equal("first", "during-import");
        Directory.EnumerateFiles(_dir).Should().BeEmpty();
    }

    [Fact]
    public async Task ImportAsync_LeftoverClaimedFileFromCrash_IsImportedAndDeleted()
    {
        var leftover = Path.Combine(_dir, "queue-fallback.0123abcd.importing");
        await File.WriteAllTextAsync(leftover, Line("left-over") + Environment.NewLine);

        await FallbackEventImporter.ImportAsync(_dir, _queue, _logger);

        _enqueuedIds.Should().Equal("left-over");
        File.Exists(leftover).Should().BeFalse();
    }

    [Fact]
    public async Task ImportAsync_EnqueueThrows_KeepsClaimedFileForRetry()
    {
        await File.WriteAllTextAsync(FallbackPath, Line("retry-me") + Environment.NewLine);
        _queue.EnqueueAsync(Arg.Any<QueuedEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("database is locked"));

        await FallbackEventImporter.ImportAsync(_dir, _queue, _logger);

        Directory.EnumerateFiles(_dir, "*.importing").Should().ContainSingle();

        _enqueuedIds.Clear();
        _queue.EnqueueAsync(Arg.Any<QueuedEvent>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        await FallbackEventImporter.ImportAsync(_dir, _queue, _logger);

        _enqueuedIds.Should().Equal("retry-me");
        Directory.EnumerateFiles(_dir).Should().BeEmpty();
    }

    [Fact]
    public async Task ImportAsync_OnlyMalformedLines_DeletesClaimedFile()
    {
        await File.WriteAllTextAsync(FallbackPath, "not json" + Environment.NewLine);

        await FallbackEventImporter.ImportAsync(_dir, _queue, _logger);

        _enqueuedIds.Should().BeEmpty();
        Directory.EnumerateFiles(_dir).Should().BeEmpty("a file that can never parse must not be retried every poll");
    }
}
