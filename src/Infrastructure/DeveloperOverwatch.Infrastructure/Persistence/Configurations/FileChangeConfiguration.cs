namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class FileChangeConfiguration : IEntityTypeConfiguration<FileChange>
{
    public void Configure(EntityTypeBuilder<FileChange> builder)
    {
        builder.HasKey(f => f.Id);
        builder.Property(f => f.FilePath).IsRequired();
        builder.Property(f => f.ChangeType).HasConversion<string>();
        builder.HasOne(f => f.Session)
               .WithMany(s => s.FileChanges)
               .HasForeignKey(f => f.SessionId);
        builder.HasOne(f => f.ToolExecution)
               .WithMany(t => t.FileChanges)
               .HasForeignKey(f => f.ToolExecutionId);
        builder.HasIndex(f => f.SessionId);
    }
}
