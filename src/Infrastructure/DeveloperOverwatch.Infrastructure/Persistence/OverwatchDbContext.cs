namespace DeveloperOverwatch.Infrastructure.Persistence;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

public class OverwatchDbContext : DbContext, IUnitOfWork
{
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<ConversationTurn> ConversationTurns => Set<ConversationTurn>();
    public DbSet<ToolExecution> ToolExecutions => Set<ToolExecution>();
    public DbSet<FileChange> FileChanges => Set<FileChange>();
    public DbSet<GovernanceViolation> GovernanceViolations => Set<GovernanceViolation>();
    public DbSet<CommitSessionLink> CommitSessionLinks => Set<CommitSessionLink>();
    public DbSet<PullRequestReview> PullRequestReviews => Set<PullRequestReview>();
    public DbSet<ArchitectAnnotation> ArchitectAnnotations => Set<ArchitectAnnotation>();
    public DbSet<SessionToolFailure> SessionToolFailures => Set<SessionToolFailure>();
    public DbSet<ProcessedAgentEvent> ProcessedAgentEvents => Set<ProcessedAgentEvent>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<DeveloperPlan> DeveloperPlans => Set<DeveloperPlan>();

    public OverwatchDbContext(DbContextOptions<OverwatchDbContext> options) : base(options) { }

    /// <summary>For provider-specific subclasses such as <see cref="SqliteOverwatchDbContext"/>.</summary>
    protected OverwatchDbContext(DbContextOptions options) : base(options) { }

    public async Task<SaveOutcome> SaveChangesDetectingDuplicateAsync(CancellationToken ct = default)
    {
        try
        {
            await SaveChangesAsync(ct);
            return SaveOutcome.Saved;
        }
        catch (DbUpdateException ex) when (UniqueConstraintViolation.Is(ex))
        {
            // The transaction rolled back, so none of these rows exist; tracked, a later save would retry them.
            foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
            return SaveOutcome.Duplicate;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OverwatchDbContext).Assembly);
    }
}
