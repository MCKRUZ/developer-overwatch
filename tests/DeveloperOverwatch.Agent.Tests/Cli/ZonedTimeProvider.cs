namespace DeveloperOverwatch.Agent.Tests.Cli;

/// <summary>A fixed clock in a fixed zone, so local-day boundaries never depend on the test machine.</summary>
internal sealed class ZonedTimeProvider(DateTime nowUtc, TimeZoneInfo zone) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), TimeSpan.Zero);

    public override TimeZoneInfo LocalTimeZone => zone;
}
