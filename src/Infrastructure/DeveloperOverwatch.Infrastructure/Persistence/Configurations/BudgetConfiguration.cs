namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> builder)
    {
        builder.ToTable("Budgets");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Scope).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(b => b.DeveloperEmail).HasMaxLength(EmailLimits.MaxLength);
        builder.Property(b => b.SubjectKey).HasMaxLength(EmailLimits.MaxLength).IsRequired();
        builder.Property(b => b.Period).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(b => b.AmountUsd).HasPrecision(18, 6);
        builder.Property(b => b.CreatedAt).HasConversion<UtcDateTimeConverter>().IsRequired();
        builder.Property(b => b.UpdatedAt).HasConversion<UtcDateTimeConverter>().IsRequired();
        // On the non-null key, not the nullable email: SQLite treats NULLs in a unique index as distinct.
        builder.HasIndex(b => new { b.SubjectKey, b.Period }).IsUnique();
    }
}
