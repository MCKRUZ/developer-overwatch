namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class PullRequestReviewConfiguration : IEntityTypeConfiguration<PullRequestReview>
{
    public void Configure(EntityTypeBuilder<PullRequestReview> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Repo).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().IsRequired();
        builder.HasIndex(r => new { r.Repo, r.PrNumber });
    }
}
