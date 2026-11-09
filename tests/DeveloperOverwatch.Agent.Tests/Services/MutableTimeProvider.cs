namespace DeveloperOverwatch.Agent.Tests.Services;

/// <summary>A clock the test sets by hand, so retention tests never depend on the real date.</summary>
internal sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
