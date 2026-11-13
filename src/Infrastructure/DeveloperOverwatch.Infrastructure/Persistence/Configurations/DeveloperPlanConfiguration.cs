namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class DeveloperPlanConfiguration : IEntityTypeConfiguration<DeveloperPlan>
{
    public void Configure(EntityTypeBuilder<DeveloperPlan> builder)
    {
        builder.ToTable("DeveloperPlans");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.DeveloperEmail).HasMaxLength(EmailLimits.MaxLength).IsRequired();
        builder.Property(p => p.PlanName).HasMaxLength(DeveloperPlan.PlanNameMaxLength).IsRequired();
        builder.Property(p => p.MonthlyPriceUsd).HasPrecision(18, 6);
        builder.Property(p => p.WindowLimitUsd).HasPrecision(18, 6);
        builder.Property(p => p.CreatedAt).HasConversion<UtcDateTimeConverter>().IsRequired();
        builder.Property(p => p.UpdatedAt).HasConversion<UtcDateTimeConverter>().IsRequired();
        builder.HasIndex(p => p.DeveloperEmail).IsUnique();
    }
}
