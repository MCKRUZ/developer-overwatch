namespace DeveloperOverwatch.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OverwatchDbContext>
{
    public OverwatchDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OverwatchDbContext>();
        optionsBuilder.UseSqlServer("Server=.;Database=overwatch-design;Trusted_Connection=True;TrustServerCertificate=True;");
        return new OverwatchDbContext(optionsBuilder.Options);
    }
}
