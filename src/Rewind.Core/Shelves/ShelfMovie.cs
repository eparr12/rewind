namespace Rewind.Core.Shelves;

/// <summary>
/// One movie on one shelf. A movie can be on any number of shelves, such as The Nightmare Before Christmas on both
/// Halloween and Christmas, and on each one once (DATA-11). The rows are what's on the shelves now, not their
/// history, so taking a movie off a shelf deletes its row, as a refresh replaces a movie's genres, and DATA-4 doesn't
/// keep it (ADR-0015).
/// </summary>
public sealed class ShelfMovie
{
    /// <summary>The shelf.</summary>
    public int ShelfId { get; set; }

    /// <summary>A movie on it.</summary>
    public int MovieId { get; set; }
}
