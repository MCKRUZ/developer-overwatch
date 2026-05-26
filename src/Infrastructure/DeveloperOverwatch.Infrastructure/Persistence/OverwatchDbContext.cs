namespace DeveloperOverwatch.Infrastructure.Persistence;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

public sealed class OverwatchDbContext : DbContext, IUnitOfWork
{
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<ConversationTurn> ConversationTurns => Set<ConversationTurn>();
    public DbSet<ToolExecution> ToolExecutions => Set<ToolExecution>();
    public DbSet<FileChange> FileChanges => Set<FileChange>();
    public DbSet<GovernanceViolation> GovernanceViolations => Set<GovernanceViolation>();
    public DbSet<CommitSessionLink> CommitSessionLinks => Set<CommitSessionLink>();
    public DbSet<PullRequestReview> PullRequestReviews => Set<PullRequestReview>();
    public DbSet<ArchitectAnnotation> ArchitectAnnotations => Set<ArchitectAnnotation>();

    public OverwatchDbContext(DbContextOptions<OverwatchDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OverwatchDbContext).Assembly);
    }
}
