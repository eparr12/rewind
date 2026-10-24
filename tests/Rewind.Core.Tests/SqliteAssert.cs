using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rewind.Core.Tests;

/// <summary>
/// Checks that SQLite itself refused a save, and which kind of constraint refused it, so a test of
/// one rule can't pass because a different rule fired (TEST-7).
/// </summary>
internal static class SqliteAssert
{
    public static async Task RefusesAsync(SqliteConstraint constraint, Func<Task> save)
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(save);
        var refusal = Assert.IsType<SqliteException>(error.InnerException);
        Assert.Equal(constraint, (SqliteConstraint)refusal.SqliteExtendedErrorCode);
    }
}
