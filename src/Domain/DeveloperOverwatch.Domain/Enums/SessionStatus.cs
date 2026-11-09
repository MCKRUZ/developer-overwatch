namespace DeveloperOverwatch.Domain.Enums;

// "Stale" is never persisted on a Session row — it's a read-time overlay applied by query
// handlers when a stored Active session has had no activity past the configured threshold.
// The stored Status stays Active either way, so a resume reactivates with zero special-casing.
public enum SessionStatus { Active, Completed, Failed, Stale }
