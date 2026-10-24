namespace Rewind.Core.Intake;

/// <summary>
/// Whether a movie typed in by hand can go in the case, and if not, what to fix (NET-12). Each
/// field may be left blank, except the title.
/// </summary>
public enum HandEntryStatus
{
    /// <summary>The movie is ready to go in the case.</summary>
    Entered,

    /// <summary>The title is blank.</summary>
    NoTitle,

    /// <summary>The year isn't a year in digits, from 1888, the oldest film's, to next year.</summary>
    NotAYear,

    /// <summary>The runtime isn't a whole number of minutes in digits, from 1 to 600.</summary>
    NotARuntime,

    /// <summary>The rating isn't one of the US ratings Rewind keeps, such as PG-13.</summary>
    NotARating,
}
