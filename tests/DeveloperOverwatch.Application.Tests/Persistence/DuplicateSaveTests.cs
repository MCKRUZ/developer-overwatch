namespace DeveloperOverwatch.Application.Tests.Persistence;

using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>Two requests can both pass a create handler's "already exists?" check before either saves. The unique
/// index then refuses the second insert, and that must reach the caller as a conflict, not as a server error.</summary>
public sealed class DuplicateSaveTests : IAsyncLifetime
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
    public async Task SaveChangesDetectingDuplicate_SecondTeamDailyBudget_ReportsDuplicateAndForgetsTheInsert()
    {
        await using var db = NewContext();
        db.Budgets.Add(Budget.Create(BudgetScope.Team, null, BudgetPeriod.Daily, 60m, Now));

        var outcome = await db.SaveChangesDetectingDuplicateAsync();

        outcome.Should().Be(SaveOutcome.Duplicate);
        // Left tracked, the refused row would be inserted again by any later save in the same request.
        db.ChangeTracker.Entries().Should().BeEmpty();
        (await db.SaveChangesAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SaveChangesDetectingDuplicate_SecondPlanForSameEmail_ReportsDuplicate()
    {
        await using (var first = NewContext())
        {
            first.DeveloperPlans.Add(DeveloperPlan.Create("dup@team.dev", "Pro", 20m, null, Now));
            (await first.SaveChangesDetectingDuplicateAsync()).Should().Be(SaveOutcome.Saved);
        }

        await using var second = NewContext();
        second.DeveloperPlans.Add(DeveloperPlan.Create("dup@team.dev", "Max", 100m, null, Now));

        (await second.SaveChangesDetectingDuplicateAsync()).Should().Be(SaveOutcome.Duplicate);
    }

    [Fact]
    public async Task SaveChangesDetectingDuplicate_NewSubject_Saves()
    {
        await using var db = NewContext();
        db.Budgets.Add(Budget.Create(BudgetScope.Developer, "new@team.dev", BudgetPeriod.Daily, 5m, Now));

        (await db.SaveChangesDetectingDuplicateAsync()).Should().Be(SaveOutcome.Saved);
        await using var check = NewContext();
        (await check.Budgets.CountAsync()).Should().Be(2, "the seeded team budget plus the new one");
    }
}
