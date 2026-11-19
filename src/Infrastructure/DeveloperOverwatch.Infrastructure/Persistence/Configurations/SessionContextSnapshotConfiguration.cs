namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class SessionContextSnapshotConfiguration : IEntityTypeConfiguration<SessionContextSnapshot>
{
    public void Configure(EntityTypeBuilder<SessionContextSnapshot> builder)
    {
        builder.ToTable("SessionContextSnapshots");
        builder.HasKey(s => s.SessionId);
        builder.HasOne<Session>()
               .WithOne()
               .HasForeignKey<SessionContextSnapshot>(s => s.SessionId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.Property(s => s.TakenAt).HasConversion<UtcDateTimeConverter>().IsRequired();
        builder.Property(s => s.ClaudeCodeVersion).HasMaxLength(ContextSnapshotLimits.ClaudeCodeVersionLength).IsRequired();
        builder.Property(s => s.Model).HasMaxLength(ContextSnapshotLimits.NameLength).IsRequired();

        builder.OwnsMany(s => s.Categories, c =>
        {
            c.ToTable("SessionContextCategories");
            OwnedRow(c);
            c.Property(x => x.Category).HasConversion<string>().HasMaxLength(32).IsRequired();
            c.Property(x => x.Server).HasMaxLength(ContextSnapshotLimits.NameLength);
        });
        builder.OwnsMany(s => s.Tools, t =>
        {
            t.ToTable("SessionContextTools");
            OwnedRow(t);
            t.Property(x => x.Group).HasMaxLength(ContextSnapshotLimits.NameLength).IsRequired();
        });
        builder.OwnsMany(s => s.Compactions, c =>
        {
            c.ToTable("SessionContextCompactions");
            OwnedRow(c);
            c.Property(x => x.At).HasConversion<UtcDateTimeConverter>().IsRequired();
            c.Property(x => x.Trigger).HasMaxLength(ContextSnapshotLimits.TriggerLength).IsRequired();
        });
    }

    // Each row gets its own database-generated key (identity on SQL Server, rowid on SQLite) instead of the
    // default composite of the owner's key and a number, so replacing a collection is a plain delete and insert.
    private static void OwnedRow<T>(OwnedNavigationBuilder<SessionContextSnapshot, T> row)
        where T : class
    {
        row.WithOwner().HasForeignKey("SessionId");
        row.Property<long>("Id");
        row.HasKey("Id");
    }
}
