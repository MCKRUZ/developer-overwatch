namespace DeveloperOverwatch.Domain.Metering;

/// <summary>
/// The usage window a moment falls inside, as computed by <see cref="UsageWindow.Current"/>.
/// This is an estimate reconstructed from observed activity, not a figure reported by the provider.
/// </summary>
/// <param name="Start">UTC timestamp of the activity that opened the window.</param>
/// <param name="ResetsAt">UTC moment the window ends and its allowance resets (exclusive).</param>
public sealed record UsageWindowResult(DateTime Start, DateTime ResetsAt);
