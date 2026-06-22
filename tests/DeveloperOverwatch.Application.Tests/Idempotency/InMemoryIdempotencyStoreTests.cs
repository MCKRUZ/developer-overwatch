namespace DeveloperOverwatch.Application.Tests.Idempotency;

using DeveloperOverwatch.Application.Idempotency;
using FluentAssertions;
using Xunit;

public class InMemoryIdempotencyStoreTests
{
    private sealed class StubTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task TryGet_UnknownKey_ReturnsNull()
    {
        var store = new InMemoryIdempotencyStore(new StubTimeProvider());

        var result = await store.TryGetAsync("missing", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Set_ThenGet_ReturnsSameReference()
    {
        var store = new InMemoryIdempotencyStore(new StubTimeProvider());
        var response = new object();

        await store.SetAsync("key", response, CancellationToken.None);
        var result = await store.TryGetAsync("key", CancellationToken.None);

        result.Should().BeSameAs(response);
    }

    [Fact]
    public async Task Get_AfterTtlElapses_ReturnsNull()
    {
        var time = new StubTimeProvider();
        var store = new InMemoryIdempotencyStore(time);

        await store.SetAsync("key", new object(), CancellationToken.None);
        time.Now += InMemoryIdempotencyStore.DefaultTtl + TimeSpan.FromSeconds(1);

        var result = await store.TryGetAsync("key", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Get_JustBeforeTtl_StillReturnsValue()
    {
        var time = new StubTimeProvider();
        var store = new InMemoryIdempotencyStore(time);

        await store.SetAsync("key", new object(), CancellationToken.None);
        time.Now += InMemoryIdempotencyStore.DefaultTtl - TimeSpan.FromSeconds(1);

        var result = await store.TryGetAsync("key", CancellationToken.None);

        result.Should().NotBeNull();
    }
}
