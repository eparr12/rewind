namespace Rewind.Core.Tests;

/// <summary>
/// The constraints SQLite refuses a save for, as its extended result codes
/// (sqlite.org/rescode.html), so a test can tell which rule did the refusing.
/// </summary>
internal enum SqliteConstraint
{
    /// <summary>SQLITE_CONSTRAINT_CHECK: a CHECK constraint.</summary>
    Check = 275,

    /// <summary>SQLITE_CONSTRAINT_FOREIGNKEY: an insert pointing at a row that isn't there.</summary>
    ForeignKey = 787,

    /// <summary>
    /// SQLITE_CONSTRAINT_TRIGGER: a Restrict foreign key refusing a delete (DATA-4). SQLite enforces
    /// ON DELETE RESTRICT with a trigger of its own, so it reports this code rather than
    /// SQLITE_CONSTRAINT_FOREIGNKEY, though the message still says "FOREIGN KEY constraint failed".
    /// </summary>
    Restrict = 1811,

    /// <summary>SQLITE_CONSTRAINT_UNIQUE: a unique index.</summary>
    Unique = 2067,
}
