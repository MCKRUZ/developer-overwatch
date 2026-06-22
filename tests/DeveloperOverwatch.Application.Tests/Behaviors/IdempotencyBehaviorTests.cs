namespace DeveloperOverwatch.Application.Tests.Behaviors;

using DeveloperOverwatch.Application.Behaviors;
using DeveloperOverwatch.Application.Idempotency;
using DeveloperOverwatch.Domain.Common;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class IdempotencyBehaviorTests
{
    private sealed record IdempotentRequest(string IdempotencyKey)
        : IRequest<Result<int>>, IIdempotentRequest;

    private sealed record PlainRequest : IRequest<Result<int>>;

    private static IdempotencyBehavior<TRequest, Result<int>> Create<TRequest>()
        where TRequest : notnull =>
        new(new InMemoryIdempotencyStore(TimeProvider.System),
            NullLogger<IdempotencyBehavior<TRequest, Result<int>>>.Instance);

    [Fact]
    public async Task NonIdempotentRequest_AlwaysInvokesHandler()
    {
        var behavior = Create<PlainRequest>();
        var calls = 0;
        RequestHandlerDelegate<Result<int>> next = _ => { calls++; return Task.FromResult(Result<int>.Success(calls)); };

        await behavior.Handle(new PlainRequest(), next, CancellationToken.None);
        await behavior.Handle(new PlainRequest(), next, CancellationToken.None);

        calls.Should().Be(2);
    }

    [Fact]
    public async Task DuplicateRequest_ReturnsCachedResponse_WithoutReinvokingHandler()
    {
        var behavior = Create<IdempotentRequest>();
        var calls = 0;
        RequestHandlerDelegate<Result<int>> next = _ => { calls++; return Task.FromResult(Result<int>.Success(calls)); };
        var request = new IdempotentRequest("delivery-123");

        var first = await behavior.Handle(request, next, CancellationToken.None);
        var second = await behavior.Handle(request, next, CancellationToken.None);

        calls.Should().Be(1, "the second call must be served from cache");
        second.Should().BeSameAs(first);
        second.Value.Should().Be(1);
    }

    [Fact]
    public async Task FailureResult_IsNotCached_SoRetryReexecutes()
    {
        var behavior = Create<IdempotentRequest>();
        var calls = 0;
        RequestHandlerDelegate<Result<int>> next = _ =>
        {
            calls++;
            // First attempt fails (transient), retry succeeds.
            return Task.FromResult(calls == 1 ? Result<int>.Fail("transient") : Result<int>.Success(42));
        };
        var request = new IdempotentRequest("delivery-abc");

        var first = await behavior.Handle(request, next, CancellationToken.None);
        var second = await behavior.Handle(request, next, CancellationToken.None);

        first.IsSuccess.Should().BeFalse();
        second.IsSuccess.Should().BeTrue();
        second.Value.Should().Be(42);
        calls.Should().Be(2, "a failed result must not be cached");
    }

    [Fact]
    public async Task EmptyIdempotencyKey_BypassesCaching()
    {
        var behavior = Create<IdempotentRequest>();
        var calls = 0;
        RequestHandlerDelegate<Result<int>> next = _ => { calls++; return Task.FromResult(Result<int>.Success(calls)); };
        var request = new IdempotentRequest(string.Empty);

        await behavior.Handle(request, next, CancellationToken.None);
        await behavior.Handle(request, next, CancellationToken.None);

        calls.Should().Be(2);
    }
}
