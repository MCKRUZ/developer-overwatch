namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class GovernanceViolationConfiguration : IEntityTypeConfiguration<GovernanceViolation>
{
    public void Configure(EntityTypeBuilder<GovernanceViolation> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.RuleId).IsRequired();
        builder.Property(v => v.Severity).HasConversion<string>().IsRequired();
        builder.Property(v => v.Message).IsRequired();
        builder.Property(v => v.ResolutionReason).HasMaxLength(1000);
        builder.HasOne(v => v.Session)
               .WithMany(s => s.GovernanceViolations)
               .HasForeignKey(v => v.SessionId);
        builder.HasIndex(v => v.SessionId);
        builder.HasIndex(v => v.Severity);
        builder.HasIndex(v => v.CreatedAt);
    }
}
