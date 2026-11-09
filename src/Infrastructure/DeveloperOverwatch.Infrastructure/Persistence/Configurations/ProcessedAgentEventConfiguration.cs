namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class ProcessedAgentEventConfiguration : IEntityTypeConfiguration<ProcessedAgentEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedAgentEvent> builder)
    {
        builder.ToTable("ProcessedAgentEvents");
        builder.HasKey(e => e.EventId);
        builder.Property(e => e.EventId).HasMaxLength(64);
        // No foreign key, so nothing sizes this column from Sessions.Id; 450 matches that key's
        // SQL Server width and keeps the column indexable.
        builder.Property(e => e.SessionId).IsRequired().HasMaxLength(450);
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(e => e.ProcessedAt).IsRequired();
        builder.HasIndex(e => e.SessionId);
    }
}
