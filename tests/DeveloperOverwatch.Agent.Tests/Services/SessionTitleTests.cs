using System.Text.Json;
using System.Text.Json.Nodes;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>A <c>/rename</c>d title written before enrolment names a session that began before it, so it
/// stays on this machine with the rest of that session; backfill sends usage alone, never a title.</summary>
public sealed class SessionTitleTests : IDisposable
{
    private static readonly DateTime PilotSince = DateTime.UtcNow.AddMinutes(-30);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ow-title-{Guid.NewGuid():N}");
    private readonly string _transcript;
    private readonly IApiClient _api = Substitute.For<IApiClient>();
    private readonly IConversationCapture _capture = Substitute.For<IConversationCapture>();

    public SessionTitleTests()
    {
        _transcript = Path.Combine(_dir, "abc-session.jsonl");
        Directory.CreateDirectory(Path.Combine(_dir, "abc-session"));
        File.WriteAllText(_transcript, "{}");
        _api.SendSessionEventsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ApiResult.Success);
        _capture.FindSubagentTranscripts(Arg.Any<string>()).Returns([]);
        _capture.CaptureFromFileAsync(_transcript, Arg.Any<int>(), false, Arg.Any<CancellationToken>())
            .Returns(new ConversationCaptureResult([new CapturedTurn("uuid:u-1", "user", "hello", PilotSince.AddMinutes(10))], 1));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best-effort cleanup */ }
    }

    [Theory]
    [InlineData(-5, true, "null")]
    [InlineData(5, true, "\"renamed\"")]
    [InlineData(-5, false, "\"renamed\"")]
    public async Task Marker_SendsTheTitleOnlyWhenWrittenAfterEnrolment(int writtenMinutes, bool pilot, string expected)
    {
        WriteTitle(PilotSince.AddMinutes(writtenMinutes));

        await ShipAsync($$"""{"transcriptPath":{{JsonSerializer.Serialize(_transcript)}}}""", pilot ? PilotSince : null);

        SentBody().Should().Contain($"\"sessionTitle\":{expected}");
    }

    [Fact]
    public async Task BackfillMarker_NeverSendsATitle()
    {
        WriteTitle(PilotSince.AddMinutes(5));

        await ShipAsync($$"""{"transcriptPath":{{JsonSerializer.Serialize(_transcript)}},"isBackfill":true}""", PilotSince);

        SentBody().Should().Contain("\"sessionTitle\":null").And.NotContain("renamed");
    }

    [Theory]
    [InlineData("after", true, true)]
    [InlineData("before", true, false)]
    [InlineData("missing", true, false)]
    [InlineData("garbage", true, false)]
    [InlineData("missing", false, true)]
    public void Enrich_KeepsAStartTitleOnlyWhenWrittenAfterEnrolment(string writtenAt, bool pilot, bool kept)
    {
        var body = new JsonObject { ["sessionId"] = "s", ["title"] = "renamed" };
        if (writtenAt != "missing")
            body["titleWrittenAt"] = writtenAt switch
            {
                "after" => PilotSince.AddMinutes(5).ToString("o"),
                "before" => PilotSince.AddMinutes(-5).ToString("o"),
                _ => "not a date"
            };

        var enriched = JsonNode.Parse(SessionStartPayload.Enrich(body.ToJsonString(), "key", "e1", new OpenSession("s", pilot ? PilotSince : DateTime.MinValue)))!.AsObject();

        enriched.ContainsKey("title").Should().Be(kept);
        enriched.ContainsKey("titleWrittenAt").Should().BeFalse();
    }

    private void WriteTitle(DateTime writtenAt)
    {
        var path = Path.Combine(_dir, "abc-session", "custom-title.json");
        File.WriteAllText(path, "{\"customTitle\":\"renamed\"}");
        File.SetLastWriteTimeUtc(path, writtenAt);
    }

    private async Task ShipAsync(string markerPayload, DateTime? pilotSince)
    {
        var redactor = Substitute.For<IPrivacyRedactor>();
        redactor.Redact(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        var shipper = new ConversationShipper(
            _api, redactor, _capture, Substitute.For<IConversationCursorStore>(), Substitute.For<IUsageStore>(),
            NullLogger.Instance, localMode: false, pilotSince);
        var marker = new QueuedEvent
        {
            Id = "m", SessionId = "s", EventType = EventType.ConversationTurn, PayloadJson = markerPayload, CreatedAt = DateTime.UtcNow
        };

        await shipper.ShipAsync(new OpenSession("s", pilotSince ?? DateTime.MinValue), marker, CancellationToken.None);
    }

    private string SentBody() =>
        _api.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IApiClient.SendSessionEventsAsync))
            .Select(c => (string)c.GetArguments()[1]!)
            .Should().ContainSingle().Subject;
}
