namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class ProcessedAgentEventConfiguration : IEntityTypeConfiguration<ProcessedAgentEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedAgentEvent> builder)
    {
        builder.ToTable("ProcessedAgentEvents");
        // An event id dedupes within its session only, so the key is the pair. Nonclustered because
        // 450 + 64 characters exceeds SQL Server's 900-byte clustered key limit.
        builder.HasKey(e => new { e.SessionId, e.EventId }).IsClustered(false);
        builder.Property(e => e.EventId).HasMaxLength(64);
        // No foreign key, so nothing sizes this column from Sessions.Id; 450 matches that key's
        // SQL Server width.
        builder.Property(e => e.SessionId).IsRequired().HasMaxLength(450);
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(e => e.ProcessedAt).IsRequired();
    }
}
