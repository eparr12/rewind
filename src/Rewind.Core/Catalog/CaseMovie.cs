namespace Rewind.Core.Catalog;

/// <summary>
/// One movie in one case: a normal DVD has one row, and a box set or a double feature has one per
/// movie. It's a table of its own so a rental item can point at a row here, which keeps the movie
/// picked from a case one that's really in it (DATA-11).
/// </summary>
public sealed class CaseMovie
{
    /// <summary>The case.</summary>
    public int CaseId { get; set; }

    /// <summary>A movie in the case.</summary>
    public int MovieId { get; set; }
}
