namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class ArchitectAnnotationConfiguration : IEntityTypeConfiguration<ArchitectAnnotation>
{
    public void Configure(EntityTypeBuilder<ArchitectAnnotation> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Content).IsRequired();
        builder.Property(a => a.CreatedBy).IsRequired();
        builder.HasOne(a => a.Session)
               .WithMany(s => s.ArchitectAnnotations)
               .HasForeignKey(a => a.SessionId);
        builder.HasOne(a => a.Turn)
               .WithMany()
               .HasForeignKey(a => a.TurnId);
        builder.HasOne(a => a.ToolExecution)
               .WithMany()
               .HasForeignKey(a => a.ToolExecutionId);
    }
}
