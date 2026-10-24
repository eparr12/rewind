namespace Rewind.Core.Catalog;

/// <summary>A genre from TMDB, such as Horror. A movie can have several.</summary>
public sealed class Genre
{
    /// <summary>The database key.</summary>
    public int Id { get; set; }

    /// <summary>
    /// The genre's ID at TMDB, such as 27 for Horror. No two genres share one, so a movie's genres
    /// are matched by ID, and a genre TMDB renames keeps its movies.
    /// </summary>
    public int TmdbId { get; set; }

    /// <summary>The genre's name, such as Horror.</summary>
    public required string Name { get; set; }
}
