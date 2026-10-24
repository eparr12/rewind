namespace Rewind.Core.Tests;

/// <summary>
/// The constraints SQLite refuses a save for, as its extended result codes
/// (sqlite.org/rescode.html), so a test can tell which rule did the refusing.
/// </summary>
internal enum SqliteConstraint
{
    /// <summary>SQLITE_CONSTRAINT_CHECK: a CHECK constraint.</summary>
    Check = 275,

    /// <summary>SQLITE_CONSTRAINT_UNIQUE: a unique index.</summary>
    Unique = 2067,
}
