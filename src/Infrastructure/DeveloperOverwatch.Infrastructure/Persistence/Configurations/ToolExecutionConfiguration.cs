namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class ToolExecutionConfiguration : IEntityTypeConfiguration<ToolExecution>
{
    public void Configure(EntityTypeBuilder<ToolExecution> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.ToolName).IsRequired();
        builder.Property(t => t.Timestamp).IsRequired();
        builder.HasOne(t => t.Session)
               .WithMany(s => s.ToolExecutions)
               .HasForeignKey(t => t.SessionId);
        builder.HasOne(t => t.Turn)
               .WithMany()
               .HasForeignKey(t => t.TurnId);
        builder.HasIndex(t => t.SessionId);
    }
}
