namespace Rewind.Core.Shelves;

/// <summary>
/// When a seasonal shelf is in season: from a first day to a last, each a month and a day with no year, so it comes
/// round every year (ADR-0015). Its numbers are a <see cref="Shelf"/>'s, which the database holds to real days.
/// </summary>
/// <param name="FromMonth">The first day's month, from 1 for January to 12 for December.</param>
/// <param name="FromDay">The first day's day of its month.</param>
/// <param name="ToMonth">
/// The last day's month. It can come before <paramref name="FromMonth"/>, for a season that runs over New Year.
/// </param>
/// <param name="ToDay">The last day's day of its month.</param>
public sealed record Season(int FromMonth, int FromDay, int ToMonth, int ToDay)
{
    /// <summary>
    /// Whether the season takes in a day: from its first day to its last, both of them in it, by month and then day.
    /// When the first day comes after the last, as for December 1 to January 6, the season runs on past December 31.
    /// A season whose first and last days are one day holds that day only. The day's year doesn't matter, so February
    /// 29 is a day like any other: in a year without it, a season that ends on it ends on February 28, and one that
    /// starts on it starts on March 1.
    /// </summary>
    public bool Holds(DateOnly day)
    {
        // A month and a day compare as a pair: the month first, then the day.
        var date = (day.Month, day.Day);
        var first = (FromMonth, FromDay);
        var last = (ToMonth, ToDay);
        return first.CompareTo(last) <= 0
            ? first.CompareTo(date) <= 0 && date.CompareTo(last) <= 0
            : first.CompareTo(date) <= 0 || date.CompareTo(last) <= 0;
    }
}
