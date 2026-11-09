namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class SessionToolFailureConfiguration : IEntityTypeConfiguration<SessionToolFailure>
{
    public void Configure(EntityTypeBuilder<SessionToolFailure> builder)
    {
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasMaxLength(64);
        builder.Property(f => f.ToolName).IsRequired().HasMaxLength(255);
        builder.Property(f => f.At).IsRequired();
        builder.HasOne<Session>()
               .WithMany()
               .HasForeignKey(f => f.SessionId);
        builder.HasIndex(f => f.SessionId);
    }
}
