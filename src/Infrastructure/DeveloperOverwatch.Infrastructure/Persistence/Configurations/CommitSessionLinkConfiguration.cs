namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class CommitSessionLinkConfiguration : IEntityTypeConfiguration<CommitSessionLink>
{
    public void Configure(EntityTypeBuilder<CommitSessionLink> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.CommitSha).IsRequired();
        builder.Property(c => c.Repo).IsRequired();
        builder.HasOne(c => c.Session)
               .WithMany(s => s.CommitSessionLinks)
               .HasForeignKey(c => c.SessionId);
        builder.HasIndex(c => c.SessionId);
        builder.HasIndex(c => c.CommitSha);
    }
}
