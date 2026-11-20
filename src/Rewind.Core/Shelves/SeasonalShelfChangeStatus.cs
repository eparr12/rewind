namespace Rewind.Core.Shelves;

/// <summary>Whether a change to the seasonal shelves was saved, and if not, what to fix (NET-12).</summary>
public enum SeasonalShelfChangeStatus
{
    /// <summary>The change is saved.</summary>
    Saved,

    /// <summary>The name is blank, and nothing is saved.</summary>
    NoName,

    /// <summary>
    /// The first day isn't a day of its month: digits alone, from 1 to the month's last day, with 29 for February.
    /// Nothing is saved.
    /// </summary>
    NotAFromDay,

    /// <summary>The last day isn't a day of its month, as for <see cref="NotAFromDay"/>, and nothing is saved.</summary>
    NotAToDay,

    /// <summary>
    /// Another seasonal shelf has the name already, active or not, whatever its letter case from A to Z, and nothing
    /// is saved.
    /// </summary>
    NameInUse,
}
