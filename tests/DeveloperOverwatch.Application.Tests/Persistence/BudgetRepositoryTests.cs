namespace DeveloperOverwatch.Application.Tests.Persistence;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Infrastructure.Persistence;
using DeveloperOverwatch.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>Budgets and plans on the migrated SQLite schema, which is what production runs: the
/// uniqueness rules must be carried by the migrations, not just the model.</summary>
public sealed class BudgetRepositoryTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 13, 9, 30, 0, DateTimeKind.Utc);
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = NewContext();
        await DatabaseInitializer.InitializeAsync(db, teamDailyBudgetSeedUsd: 0m);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private SqliteOverwatchDbContext NewContext() =>
        new(new DbContextOptionsBuilder<SqliteOverwatchDbContext>().UseSqlite(_connection).Options);

    // SQLite treats NULLs in a unique index as distinct, so this only holds because of SubjectKey.
    [Fact]
    public async Task SaveChanges_SecondTeamDailyBudget_IsRejectedBySqlite()
    {
        await AddBudgetAsync(Budget.Create(BudgetScope.Team, null, BudgetPeriod.Daily, 50m, Now));

        var act = () => AddBudgetAsync(Budget.Create(BudgetScope.Team, null, BudgetPeriod.Daily, 60m, Now));

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task SaveChanges_SameDeveloperAndPeriodWithStraySpaces_IsRejected()
    {
        await AddBudgetAsync(Budget.Create(BudgetScope.Developer, "dev@team.dev", BudgetPeriod.Monthly, 50m, Now));

        var act = () => AddBudgetAsync(
            Budget.Create(BudgetScope.Developer, " dev@team.dev ", BudgetPeriod.Monthly, 60m, Now));

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task SaveChanges_SameSubjectDifferentPeriod_IsAllowed()
    {
        await AddBudgetAsync(Budget.Create(BudgetScope.Team, null, BudgetPeriod.Daily, 50m, Now));
        await AddBudgetAsync(Budget.Create(BudgetScope.Team, null, BudgetPeriod.Monthly, 900m, Now));

        await using var db = NewContext();
        (await new BudgetRepository(db).GetAllAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task SaveChanges_SecondPlanForSameEmail_IsRejected()
    {
        await AddPlanAsync(DeveloperPlan.Create("dev@team.dev", "Pro", 20m, null, Now));

        var act = () => AddPlanAsync(DeveloperPlan.Create(" dev@team.dev", "Max", 100m, 40m, Now));

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // Enums are stored as their names, so a SQL ORDER BY would put "Developer" before "Team".
    [Fact]
    public async Task GetAllAsync_OrdersTeamFirstThenEmailThenDailyBeforeMonthly()
    {
        await AddBudgetAsync(Budget.Create(BudgetScope.Developer, "zed@team.dev", BudgetPeriod.Daily, 1m, Now));
        await AddBudgetAsync(Budget.Create(BudgetScope.Developer, "amy@team.dev", BudgetPeriod.Monthly, 1m, Now));
        await AddBudgetAsync(Budget.Create(BudgetScope.Team, null, BudgetPeriod.Monthly, 1m, Now));
        await AddBudgetAsync(Budget.Create(BudgetScope.Developer, "amy@team.dev", BudgetPeriod.Daily, 1m, Now));
        await AddBudgetAsync(Budget.Create(BudgetScope.Team, null, BudgetPeriod.Daily, 1m, Now));

        await using var db = NewContext();
        var budgets = await new BudgetRepository(db).GetAllAsync();

        budgets.Select(b => $"{b.SubjectKey}/{b.Period}").Should().Equal(
            "team/Daily", "team/Monthly", "amy@team.dev/Daily", "amy@team.dev/Monthly", "zed@team.dev/Daily");
    }

    // SQLite (and SQL Server datetime2) hand back Kind Unspecified, which would serialize without the Z.
    [Fact]
    public async Task Reads_RoundTripValuesAndMarkTimesUtc()
    {
        var budget = Budget.Create(BudgetScope.Developer, "dev@team.dev", BudgetPeriod.Daily, 12.345678m, Now);
        var plan = DeveloperPlan.Create("dev@team.dev", "Max 5x", 100m, 33.5m, Now);
        await AddBudgetAsync(budget);
        await AddPlanAsync(plan);

        await using var db = NewContext();
        var readBudget = await new BudgetRepository(db).GetByIdAsync(budget.Id);
        var readPlan = await new DeveloperPlanRepository(db).GetByIdAsync(plan.Id);

        readBudget!.AmountUsd.Should().Be(12.345678m);
        readBudget.Scope.Should().Be(BudgetScope.Developer);
        readBudget.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        readBudget.UpdatedAt.Should().Be(Now);
        readBudget.UpdatedAt.Kind.Should().Be(DateTimeKind.Utc);
        readPlan!.WindowLimitUsd.Should().Be(33.5m);
        readPlan.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        readPlan.UpdatedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task FindAsync_LooksUpBySubjectKeyAndPeriod()
    {
        var teamDaily = Budget.Create(BudgetScope.Team, null, BudgetPeriod.Daily, 50m, Now);
        await AddBudgetAsync(teamDaily);
        await AddBudgetAsync(Budget.Create(BudgetScope.Team, null, BudgetPeriod.Monthly, 900m, Now));

        await using var db = NewContext();
        var repository = new BudgetRepository(db);

        (await repository.FindAsync(Budget.TeamSubjectKey, BudgetPeriod.Daily))!.Id.Should().Be(teamDaily.Id);
        (await repository.FindAsync("dev@team.dev", BudgetPeriod.Daily)).Should().BeNull();
    }

    [Fact]
    public async Task Remove_DeletesTheBudget()
    {
        var budget = Budget.Create(BudgetScope.Team, null, BudgetPeriod.Daily, 50m, Now);
        await AddBudgetAsync(budget);

        await using (var db = NewContext())
        {
            var repository = new BudgetRepository(db);
            repository.Remove((await repository.GetByIdAsync(budget.Id))!);
            await db.SaveChangesAsync();
        }

        await using var reader = NewContext();
        (await new BudgetRepository(reader).GetByIdAsync(budget.Id)).Should().BeNull();
    }

    [Fact]
    public async Task PlanRepository_OrdersByEmail_FindsByEmail_AndRemoves()
    {
        await AddPlanAsync(DeveloperPlan.Create("zed@team.dev", "Pro", 20m, null, Now));
        await AddPlanAsync(DeveloperPlan.Create("amy@team.dev", "Max", 100m, 40m, Now));

        await using var db = NewContext();
        var repository = new DeveloperPlanRepository(db);
        var all = await repository.GetAllAsync();
        all.Select(p => p.DeveloperEmail).Should().Equal("amy@team.dev", "zed@team.dev");

        var zed = await repository.FindByEmailAsync("zed@team.dev");
        zed!.PlanName.Should().Be("Pro");
        repository.Remove(zed);
        await db.SaveChangesAsync();

        (await repository.FindByEmailAsync("zed@team.dev")).Should().BeNull();
    }

    private async Task AddBudgetAsync(Budget budget)
    {
        await using var db = NewContext();
        await new BudgetRepository(db).AddAsync(budget);
        await db.SaveChangesAsync();
    }

    private async Task AddPlanAsync(DeveloperPlan plan)
    {
        await using var db = NewContext();
        await new DeveloperPlanRepository(db).AddAsync(plan);
        await db.SaveChangesAsync();
    }
}
