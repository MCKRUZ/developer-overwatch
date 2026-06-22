namespace DeveloperOverwatch.Application.Tests.Behaviors;

using System.Diagnostics;
using DeveloperOverwatch.Application;
using DeveloperOverwatch.Application.Behaviors;
using FluentAssertions;
using MediatR;
using Xunit;

public sealed class RequestTracingBehaviorTests : IDisposable
{
    private sealed record SampleRequest : IRequest<string>;

    private readonly List<Activity> _started = new();
    private readonly ActivityListener _listener;

    public RequestTracingBehaviorTests()
    {
        // Without a listener, ActivitySource.StartActivity returns null and the behavior is a no-op.
        // Subscribing here makes the span observable so we can assert on it.
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ApplicationTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => _started.Add(activity)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    private static RequestTracingBehavior<SampleRequest, string> Create() => new();

    [Fact]
    public async Task SuccessfulRequest_StartsSpan_TagsRequestType_AndSetsOkStatus()
    {
        var behavior = Create();
        RequestHandlerDelegate<string> next = _ => Task.FromResult("ok");

        var result = await behavior.Handle(new SampleRequest(), next, CancellationToken.None);

        result.Should().Be("ok");
        _started.Should().ContainSingle();
        var activity = _started[0];
        activity.DisplayName.Should().Be(nameof(SampleRequest));
        activity.GetTagItem("mediatr.request_type").Should().Be(typeof(SampleRequest).FullName);
        activity.Status.Should().Be(ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task FailingRequest_SetsErrorStatus_RecordsExceptionEvent_AndRethrows()
    {
        var behavior = Create();
        var boom = new InvalidOperationException("kaboom");
        RequestHandlerDelegate<string> next = _ => throw boom;

        var act = () => behavior.Handle(new SampleRequest(), next, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("kaboom");
        _started.Should().ContainSingle();
        var activity = _started[0];
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be("kaboom");
        var exceptionEvent = activity.Events.Should().ContainSingle(e => e.Name == "exception").Subject;
        exceptionEvent.Tags.Should().Contain(t =>
            t.Key == "exception.type" && (string?)t.Value == typeof(InvalidOperationException).FullName);
        exceptionEvent.Tags.Should().Contain(t =>
            t.Key == "exception.message" && (string?)t.Value == "kaboom");
    }
}
