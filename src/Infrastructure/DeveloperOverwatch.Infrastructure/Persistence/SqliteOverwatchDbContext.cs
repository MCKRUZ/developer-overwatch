namespace DeveloperOverwatch.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// The same model as <see cref="OverwatchDbContext"/>, used when the provider is SQLite. It exists
/// only so SQLite gets its own migration set: EF Core keys migrations by context type, and the SQL
/// Server migrations (on the base type) use column types SQLite cannot run.
/// </summary>
public sealed class SqliteOverwatchDbContext : OverwatchDbContext
{
    public SqliteOverwatchDbContext(DbContextOptions<SqliteOverwatchDbContext> options) : base(options) { }
}
