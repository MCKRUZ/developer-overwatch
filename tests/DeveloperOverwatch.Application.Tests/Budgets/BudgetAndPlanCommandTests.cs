namespace DeveloperOverwatch.Application.Tests.Budgets;

using DeveloperOverwatch.Application.Commands.CreateBudget;
using DeveloperOverwatch.Application.Commands.CreateDeveloperPlan;
using DeveloperOverwatch.Application.Commands.UpdateBudget;
using DeveloperOverwatch.Application.Commands.UpdateDeveloperPlan;
using DeveloperOverwatch.Application.Models;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Infrastructure.Persistence;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>The create handlers on the migrated SQLite schema, where a duplicate that slips past the "already
/// exists?" check (a racing request) must still come back as the conflict code rather than an exception.</summary>
public sealed class BudgetAndPlanCommandTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 13, 9, 30, 0, DateTimeKind.Utc);
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = NewContext();
        await DatabaseInitializer.InitializeAsync(db, teamDailyBudgetSeedUsd: 50m);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private SqliteOverwatchDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SqliteOverwatchDbContext>().UseSqlite(_connection).Options);

    [Fact]
    public async Task CreateBudget_DuplicateMissedByTheCheck_ReturnsAlreadyExists()
    {
        await using var db = NewContext();
        var handler = new CreateBudgetCommandHandler(
            new RacingBudgetRepository(new BudgetRepository(db)), db, TimeProvider.System);

        var result = await handler.Handle(
            new CreateBudgetCommand(BudgetScope.Team, null, BudgetPeriod.Daily, 75m), CancellationToken.None);

        result.Error.Should().Be(ErrorCodes.AlreadyExists);
    }

    [Fact]
    public async Task CreateBudget_ExistingSubjectAndPeriod_ReturnsAlreadyExistsBeforeSaving()
    {
        await using var db = NewContext();
        var handler = new CreateBudgetCommandHandler(new BudgetRepository(db), db, TimeProvider.System);

        var result = await handler.Handle(
            new CreateBudgetCommand(BudgetScope.Team, null, BudgetPeriod.Daily, 75m), CancellationToken.None);

        result.Error.Should().Be(ErrorCodes.AlreadyExists);
    }

    [Fact]
    public async Task CreateBudget_NewDeveloper_StoresTrimmedEmail()
    {
        await using var db = NewContext();
        var handler = new CreateBudgetCommandHandler(new BudgetRepository(db), db, TimeProvider.System);

        var result = await handler.Handle(
            new CreateBudgetCommand(BudgetScope.Developer, "  amy@team.dev ", BudgetPeriod.Monthly, 300m),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DeveloperEmail.Should().Be("amy@team.dev");
    }

    [Fact]
    public async Task CreatePlan_DuplicateMissedByTheCheck_ReturnsAlreadyExists()
    {
        await using (var seed = NewContext())
        {
            seed.DeveloperPlans.Add(DeveloperPlan.Create("race@team.dev", "Pro", 20m, null, Now));
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext();
        var handler = new CreateDeveloperPlanCommandHandler(
            new RacingPlanRepository(new DeveloperPlanRepository(db)), db, TimeProvider.System);

        var result = await handler.Handle(
            new CreateDeveloperPlanCommand("race@team.dev", "Max", 100m, 40m), CancellationToken.None);

        result.Error.Should().Be(ErrorCodes.AlreadyExists);
    }

    [Fact]
    public async Task Updates_UnknownId_ReturnNotFound()
    {
        await using var db = NewContext();
        var budget = await new UpdateBudgetCommandHandler(new BudgetRepository(db), db, TimeProvider.System)
            .Handle(new UpdateBudgetCommand(Guid.NewGuid(), 10m), CancellationToken.None);
        var plan = await new UpdateDeveloperPlanCommandHandler(new DeveloperPlanRepository(db), db, TimeProvider.System)
            .Handle(new UpdateDeveloperPlanCommand(Guid.NewGuid(), "Pro", 20m, null), CancellationToken.None);

        budget.Error.Should().Be(ErrorCodes.NotFound);
        plan.Error.Should().Be(ErrorCodes.NotFound);
    }

    /// <summary>The real repository, except that the "already exists?" lookup answers as if another request had
    /// not yet saved: the state a race leaves the handler in.</summary>
    private sealed class RacingBudgetRepository(IBudgetRepository inner) : IBudgetRepository
    {
        public Task<IReadOnlyList<Budget>> GetAllAsync(CancellationToken ct = default) => inner.GetAllAsync(ct);
        public Task<Budget?> GetByIdAsync(Guid id, CancellationToken ct = default) => inner.GetByIdAsync(id, ct);
        public Task<Budget?> FindAsync(string subjectKey, BudgetPeriod period, CancellationToken ct = default) =>
            Task.FromResult<Budget?>(null);
        public Task AddAsync(Budget budget, CancellationToken ct = default) => inner.AddAsync(budget, ct);
        public void Remove(Budget budget) => inner.Remove(budget);
    }

    private sealed class RacingPlanRepository(IDeveloperPlanRepository inner) : IDeveloperPlanRepository
    {
        public Task<IReadOnlyList<DeveloperPlan>> GetAllAsync(CancellationToken ct = default) => inner.GetAllAsync(ct);
        public Task<DeveloperPlan?> GetByIdAsync(Guid id, CancellationToken ct = default) => inner.GetByIdAsync(id, ct);
        public Task<DeveloperPlan?> FindByEmailAsync(string trimmedEmail, CancellationToken ct = default) =>
            Task.FromResult<DeveloperPlan?>(null);
        public Task AddAsync(DeveloperPlan plan, CancellationToken ct = default) => inner.AddAsync(plan, ct);
        public void Remove(DeveloperPlan plan) => inner.Remove(plan);
    }
}
