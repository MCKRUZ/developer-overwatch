using System.Globalization;
using DeveloperOverwatch.Agent.Models;

namespace DeveloperOverwatch.Agent.Cli;

/// <summary>
/// <c>overwatch-agent backfill [--days N]</c>: loads token usage from Claude Code transcripts written before
/// the agent was installed. Usage is recorded locally in both modes; in Pilot mode each session is also
/// queued for the processor to ship, unless live capture already owns it. Safe to run again.
/// </summary>
public static class BackfillCommand
{
    public const int DefaultDays = 30;
    public const int MaxDays = 365;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static bool TryParseDays(IReadOnlyList<string> args, out int days, out string? problem)
    {
        days = DefaultDays;
        problem = null;
        if (args.Count == 0)
            return true;

        if (args.Count == 2 && args[0] == "--days"
            && int.TryParse(args[1], NumberStyles.None, Invariant, out var parsed)
            && parsed is >= 1 and <= MaxDays)
        {
            days = parsed;
            return true;
        }

        problem = $"The backfill command takes an optional --days N, where N is 1 to {MaxDays}.";
        return false;
    }

    public static async Task<int> RunAsync(
        TextWriter output, TextWriter error, BackfillServices services, AgentConfig config, TimeProvider time,
        int days, CancellationToken ct = default)
    {
        try
        {
            if (!Directory.Exists(services.ProjectsDir))
            {
                output.WriteLine($"No Claude Code transcripts found: {services.ProjectsDir} does not exist.");
                return 0;
            }

            var nowUtc = time.GetUtcNow().UtcDateTime;
            var summary = await new BackfillRun(services, config, nowUtc, nowUtc.AddDays(-days)).ExecuteAsync(ct);
            WriteSummary(output, summary, days, services.ProjectsDir, config.Mode);
            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error.WriteLine($"Backfill failed: {ex.Message}");
            return 1;
        }
    }

    private static void WriteSummary(
        TextWriter output, BackfillSummary summary, int days, string projectsDir, AgentMode mode)
    {
        output.WriteLine($"Backfill of the last {days} days from {projectsDir}");
        output.WriteLine();
        output.WriteLine(Row("Transcripts scanned", summary.FilesScanned.ToString(Invariant)));
        output.WriteLine(Row("Skipped, too old", summary.FilesTooOld.ToString(Invariant)));
        output.WriteLine(Row("Skipped, unreadable", summary.FilesUnreadable.ToString(Invariant)));
        output.WriteLine(Row("Sessions", summary.Sessions.ToString(Invariant)));
        output.WriteLine(Row("Responses recorded", string.Format(Invariant, "{0} new, {1} already present",
            summary.ResponsesAdded, summary.ResponsesAlreadyPresent)));
        output.WriteLine(Row("Parse errors", summary.ParseErrors.ToString(Invariant)));
        if (mode == AgentMode.Pilot)
        {
            output.WriteLine(Row("Queued for shipping", summary.SessionsQueued.ToString(Invariant)));
            output.WriteLine(Row("Still active, not closed", summary.SessionsStillActive.ToString(Invariant)));
        }
    }

    private static string Row(string label, string value) => $"  {label,-26}{value}";
}
