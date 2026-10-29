using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rewind.Core.Data;

/// <summary>
/// What a save SQLite refused comes to, read from SQLite's own error code, so a service can tell a rule
/// the database holds (DATA-11) from a bug.
/// </summary>
public static class SqliteErrors
{
    /// <summary>
    /// Whether a unique index refused the save, such as a store label or a member card that another save
    /// took first. A CHECK, a foreign key or anything else SQLite refused isn't one.
    /// </summary>
    public static bool IsUniqueIndexRefusal(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.InnerException is SqliteException { SqliteExtendedErrorCode: SQLitePCL.raw.SQLITE_CONSTRAINT_UNIQUE };
    }
}
