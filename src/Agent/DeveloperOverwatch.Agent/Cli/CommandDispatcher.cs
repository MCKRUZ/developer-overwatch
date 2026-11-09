using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeveloperOverwatch.Agent.Cli;

/// <summary>
/// Runs a one-shot command verb (<c>overwatch-agent usage</c>) instead of the background agent. Program
/// calls it only when arguments are present; with none, the background agent starts as before. To add a
/// verb, add one entry to <see cref="Verbs"/>.
/// </summary>
public static class CommandDispatcher
{
    public const int UsageErrorExitCode = 2;

    private sealed record Verb(string Summary, Func<CliContext, Task<int>> RunAsync);

    private static readonly IReadOnlyDictionary<string, Verb> Verbs = new Dictionary<string, Verb>(StringComparer.Ordinal)
    {
        ["usage"] = new("Show token usage and estimated cost recorded on this machine", RunUsageAsync),
        ["backfill"] = new(
            $"Load usage from earlier Claude Code transcripts [--days N, 1-{BackfillCommand.MaxDays}, default {BackfillCommand.DefaultDays}]",
            RunBackfillAsync),
    };

    public static Task<int> RunAsync(
        IReadOnlyList<string> args, TextWriter output, TextWriter error, AgentConfig config, TimeProvider time)
    {
        if (args.Count == 0 || !Verbs.TryGetValue(args[0], out var verb))
        {
            if (args.Count > 0)
                error.WriteLine($"Unknown command '{args[0]}'.");
            WriteHelp(error);
            return Task.FromResult(UsageErrorExitCode);
        }

        return verb.RunAsync(new CliContext(args.Skip(1).ToList(), output, error, config, time));
    }

    private static async Task<int> RunUsageAsync(CliContext context)
    {
        if (context.Args.Count > 0)
        {
            context.Error.WriteLine("The usage command takes no arguments.");
            WriteHelp(context.Error);
            return UsageErrorExitCode;
        }

        using var store = new UsageStore(context.Config.QueueDbPath, context.Time);
        return await UsageCommand.RunAsync(context.Output, context.Error, store, context.Config, context.Time);
    }

    private static async Task<int> RunBackfillAsync(CliContext context)
    {
        if (!BackfillCommand.TryParseDays(context.Args, out var days, out var problem))
        {
            context.Error.WriteLine(problem);
            WriteHelp(context.Error);
            return UsageErrorExitCode;
        }

        // Opening queue.db can fail (locked, unwritable); that must end in a short message, not a stack trace.
        try
        {
            using var usage = new UsageStore(context.Config.QueueDbPath, context.Time);
            using var queue = new EventQueue(context.Config.QueueDbPath, context.Time);
            var services = new BackfillServices(
                usage, queue, new ConversationCursorStore(context.Config.OverwatchDir),
                new ConversationCapture(NullLogger<ConversationCapture>.Instance), new GitRemoteReader(),
                BackfillServices.DefaultProjectsDir);
            return await BackfillCommand.RunAsync(context.Output, context.Error, services, context.Config, context.Time, days);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.Error.WriteLine($"Backfill failed: {ex.Message}");
            return 1;
        }
    }

    private static void WriteHelp(TextWriter error)
    {
        error.WriteLine("Usage: overwatch-agent [command]");
        error.WriteLine();
        error.WriteLine($"  {"(none)",-10}Run the background agent");
        foreach (var (name, verb) in Verbs)
            error.WriteLine($"  {name,-10}{verb.Summary}");
    }
}
