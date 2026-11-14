namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

/// <summary>Marks stored times as UTC when read. SQLite and SQL Server datetime2 return Kind Unspecified,
/// which serializes without the trailing Z. Values are written unchanged: callers store UTC.</summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}
