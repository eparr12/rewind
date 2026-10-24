namespace Rewind.Core.Catalog;

/// <summary>Links a movie to one of its genres. A refresh replaces a movie's links (DATA-9).</summary>
public sealed class MovieGenre
{
    /// <summary>The movie.</summary>
    public int MovieId { get; set; }

    /// <summary>One of the movie's genres.</summary>
    public int GenreId { get; set; }
}
