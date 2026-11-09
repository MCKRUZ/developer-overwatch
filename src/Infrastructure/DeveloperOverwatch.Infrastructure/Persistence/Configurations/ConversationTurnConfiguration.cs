namespace DeveloperOverwatch.Infrastructure.Persistence.Configurations;

using DeveloperOverwatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class ConversationTurnConfiguration : IEntityTypeConfiguration<ConversationTurn>
{
    public void Configure(EntityTypeBuilder<ConversationTurn> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Role).IsRequired();
        builder.Property(t => t.Timestamp).IsRequired();
        builder.Property(t => t.EstimatedCostUsd).HasPrecision(18, 6);
        builder.HasOne(t => t.Session)
               .WithMany(s => s.ConversationTurns)
               .HasForeignKey(t => t.SessionId);
        builder.HasIndex(t => t.SessionId);
    }
}
