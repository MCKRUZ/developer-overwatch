namespace DeveloperOverwatch.Agent.Cli;

/// <summary>The counts one backfill run reports.</summary>
internal sealed record BackfillSummary(
    int FilesScanned,
    int FilesTooOld,
    int Sessions,
    int ResponsesAdded,
    int ResponsesAlreadyPresent,
    int ParseErrors,
    int SessionsQueued);
