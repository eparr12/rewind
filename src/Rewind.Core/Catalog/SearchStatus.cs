namespace Rewind.Core.Catalog;

/// <summary>Whether a search of the catalog ran, and if not, what to fix (NET-12).</summary>
public enum SearchStatus
{
    /// <summary>The search ran, whatever it found.</summary>
    Searched,

    /// <summary>The year isn't a year in digits, from 1888, the oldest film's, to next year, so nothing was searched.</summary>
    NotAYear,

    /// <summary>The rating isn't one of the US ratings Rewind keeps, such as PG-13, so nothing was searched.</summary>
    NotARating,
}
