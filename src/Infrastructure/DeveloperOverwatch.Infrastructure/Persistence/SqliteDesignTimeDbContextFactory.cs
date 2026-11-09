namespace DeveloperOverwatch.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

/// <summary>Lets <c>dotnet ef migrations add --context SqliteOverwatchDbContext</c> build the SQLite migration set.</summary>
public sealed class SqliteDesignTimeDbContextFactory : IDesignTimeDbContextFactory<SqliteOverwatchDbContext>
{
    public SqliteOverwatchDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SqliteOverwatchDbContext>();
        optionsBuilder.UseSqlite("Data Source=overwatch-design.db");
        return new SqliteOverwatchDbContext(optionsBuilder.Options);
    }
}
