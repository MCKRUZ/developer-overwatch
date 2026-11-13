namespace DeveloperOverwatch.Domain.Common;

/// <summary>How budgets and plans store a developer's email. Sessions carry whatever case the developer's git config
/// uses, so stored emails are lower-cased and every match against session emails ignores case. Lower-casing also
/// makes the unique indexes agree across SQLite (case-sensitive) and SQL Server (case-insensitive by default).</summary>
public static class DeveloperEmail
{
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
}
