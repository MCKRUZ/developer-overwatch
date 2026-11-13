namespace DeveloperOverwatch.Infrastructure.Persistence;

using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

/// <summary>Recognises a save refused by a unique index on either provider, and nothing else: NOT NULL, foreign-key
/// and check failures share SQLite's primary error code, so only the extended code is trusted.</summary>
internal static class UniqueConstraintViolation
{
    private const int SqliteConstraintUnique = 2067;
    private const int SqlServerUniqueIndex = 2601;
    private const int SqlServerUniqueConstraint = 2627;

    public static bool Is(DbUpdateException exception) => exception.InnerException switch
    {
        SqliteException sqlite => sqlite.SqliteExtendedErrorCode == SqliteConstraintUnique,
        SqlException sqlServer => sqlServer.Number is SqlServerUniqueIndex or SqlServerUniqueConstraint,
        _ => false,
    };
}
