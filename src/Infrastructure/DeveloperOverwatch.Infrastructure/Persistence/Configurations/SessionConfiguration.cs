namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.DeveloperEmail).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().IsRequired();
        builder.Property(s => s.StartedAt).IsRequired();
        builder.Property(s => s.EstimatedCostUsd).HasPrecision(18, 6);
        builder.Property(s => s.Agent).IsRequired().HasMaxLength(64).HasDefaultValue(Session.DefaultAgent);
        builder.Property(s => s.MachineName).HasMaxLength(255);
        builder.Property(s => s.ProjectKey).HasMaxLength(512);
        builder.Property(s => s.ActivityState).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.LastToolErrorName).HasMaxLength(255);
        builder.HasIndex(s => s.DeveloperEmail);
        builder.HasIndex(s => s.Status);
    }
}
