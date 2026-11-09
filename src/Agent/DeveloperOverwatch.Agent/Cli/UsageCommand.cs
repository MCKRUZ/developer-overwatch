using System.Globalization;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using DeveloperOverwatch.Domain.Metering;

namespace DeveloperOverwatch.Agent.Cli;

/// <summary>
/// <c>overwatch-agent usage</c>: prints the token usage kept in this machine's local usage table, with
/// estimated cost at API list prices. Plain ASCII, times in the local zone of <see cref="TimeProvider"/>.
/// </summary>
public static class UsageCommand
{
    private const string PeriodRow = "{0,-12}{1,14}{2,14}{3,14}{4,14}{5,16}{6,12}";
    private const string ModelRow = "{0,-32}{1,16}{2,12}";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static async Task<int> RunAsync(
        TextWriter output, TextWriter error, IUsageStore store, AgentConfig config, TimeProvider time,
        CancellationToken ct = default)
    {
        if (!UsageSettings.TryLoad(config, out var settings, out var problem))
        {
            error.WriteLine(problem);
            return 1;
        }

        await store.InitializeAsync(ct);
        var zone = time.LocalTimeZone;
        var periods = UsagePeriods.For(time.GetUtcNow().UtcDateTime, zone);
        var rows = await store.GetUsageAsync(periods.QueryStartUtc, periods.NowUtc.AddTicks(1), ct);

        if (rows.Count == 0)
            output.WriteLine("No usage recorded yet.");
        else
            WriteReport(output, Price(rows, settings), settings.Budget, periods, zone);

        output.WriteLine();
        output.WriteLine(
            $"Estimates at API list prices as of {settings.Prices.PricesAsOf.ToString("yyyy-MM-dd", Invariant)}. Mode: {config.Mode}.");
        return 0;
    }

    /// <summary>Each row is priced with its own model and prompt size, so a mixed-model day sums correctly.</summary>
    private static List<(UsageRecord Row, decimal Usd)> Price(IReadOnlyList<UsageRecord> rows, UsageSettings settings) =>
        rows.Select(r => (r, settings.Prices.CalculateCost(
                r.Model, r.InputTokens, r.OutputTokens, r.CacheCreationTokens, r.CacheReadTokens)))
            .ToList();

    private static void WriteReport(
        TextWriter output, List<(UsageRecord Row, decimal Usd)> priced, BudgetSettings budget, UsagePeriods periods,
        TimeZoneInfo zone)
    {
        var today = Sum(priced.Where(p => p.Row.Timestamp >= periods.TodayStartUtc));
        var week = Sum(priced.Where(p => p.Row.Timestamp >= periods.WeekStartUtc));
        var monthRows = priced.Where(p => p.Row.Timestamp >= periods.MonthStartUtc).ToList();
        var month = Sum(monthRows);

        output.WriteLine($"Token usage (times are local, {OffsetLabel(zone, periods.NowUtc)})");
        output.WriteLine();
        output.WriteLine(string.Format(Invariant, PeriodRow, "Period", "Input", "Output", "Cache write", "Cache read", "Total", "Est. USD"));
        WritePeriod(output, "Today", today);
        WritePeriod(output, "This week", week);
        WritePeriod(output, "This month", month);

        output.WriteLine();
        WriteModelSplit(output, monthRows);
        output.WriteLine();
        WriteWindow(output, priced, periods, zone);
        WriteBudget(output, budget, today.Usd, month.Usd);
    }

    private static void WritePeriod(TextWriter output, string label, TokenTotals totals) =>
        output.WriteLine(string.Format(Invariant, PeriodRow, label,
            Count(totals.Input), Count(totals.Output), Count(totals.CacheWrite), Count(totals.CacheRead),
            Count(totals.Total), Usd(totals.Usd)));

    private static void WriteModelSplit(TextWriter output, List<(UsageRecord Row, decimal Usd)> monthRows)
    {
        output.WriteLine("By model, this month");
        output.WriteLine(string.Format(Invariant, ModelRow, "Model", "Tokens", "Est. USD"));

        var byModel = monthRows
            .GroupBy(p => p.Row.Model ?? "(unknown)", StringComparer.Ordinal)
            .Select(g => (Model: g.Key, Totals: Sum(g)))
            .OrderByDescending(m => m.Totals.Usd)
            .ThenBy(m => m.Model, StringComparer.Ordinal);

        foreach (var (model, totals) in byModel)
            output.WriteLine(string.Format(Invariant, ModelRow, model, Count(totals.Total), Usd(totals.Usd)));
    }

    private static void WriteWindow(
        TextWriter output, List<(UsageRecord Row, decimal Usd)> priced, UsagePeriods periods, TimeZoneInfo zone)
    {
        var activity = priced.Where(p => p.Row.Timestamp >= periods.WindowActivityStartUtc).Select(p => p.Row.Timestamp);
        var window = UsageWindow.Current(activity, periods.NowUtc);
        if (window is null)
        {
            output.WriteLine("Current 5-hour window: no active window");
            return;
        }

        var soFar = Sum(priced.Where(p => p.Row.Timestamp >= window.Start && p.Row.Timestamp <= periods.NowUtc));
        output.WriteLine("Current 5-hour window");
        output.WriteLine($"  Started {LocalTime(window.Start, zone)}, resets {LocalTime(window.ResetsAt, zone)}");
        output.WriteLine($"  {Count(soFar.Total)} tokens, est. {Usd(soFar.Usd)} so far");
    }

    private static void WriteBudget(TextWriter output, BudgetSettings budget, decimal todayUsd, decimal monthUsd)
    {
        var lines = new List<string>();
        if (budget.DailyUsd is decimal daily && daily > 0)
            lines.Add(BudgetLine("Daily", todayUsd, daily));
        if (budget.MonthlyUsd is decimal monthly && monthly > 0)
            lines.Add(BudgetLine("Monthly", monthUsd, monthly));
        if (lines.Count == 0)
            return;

        output.WriteLine();
        output.WriteLine("Budget");
        lines.ForEach(output.WriteLine);
    }

    private static string BudgetLine(string label, decimal spent, decimal limit) =>
        string.Format(Invariant, "  {0,-9}{1} / {2} ({3:0.0}%)", label, Usd(spent), Usd(limit), spent / limit * 100m);

    private static TokenTotals Sum(IEnumerable<(UsageRecord Row, decimal Usd)> rows) =>
        rows.Aggregate(TokenTotals.Zero, (totals, p) => totals.Add(p.Row, p.Usd));

    private static string Count(long tokens) => tokens.ToString("N0", Invariant);

    private static string Usd(decimal amount) => "$" + amount.ToString("N2", Invariant);

    private static string LocalTime(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, zone).ToString("yyyy-MM-dd HH:mm", Invariant);

    private static string OffsetLabel(TimeZoneInfo zone, DateTime nowUtc)
    {
        var offset = zone.GetUtcOffset(nowUtc);
        return "UTC" + (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString(@"hh\:mm", Invariant);
    }
}
