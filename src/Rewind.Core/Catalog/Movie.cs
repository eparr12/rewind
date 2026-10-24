namespace Rewind.Core.Catalog;

/// <summary>
/// One film, however many cases it's in. A case holds one movie or several, and a movie can be in
/// several cases, such as two editions, through <see cref="CaseMovie"/>. TMDB's details live here
/// and in the movie's cast and genres, so a refresh never touches what the family entered (DATA-9).
/// </summary>
public sealed class Movie
{
    /// <summary>The database key.</summary>
    public int Id { get; set; }

    /// <summary>
    /// The movie's ID at TMDB, such as 948 for Halloween (1978). No two movies share one, so a new
    /// edition joins the movie we already have. Null for a movie entered by hand.
    /// </summary>
    public int? TmdbId { get; set; }

    /// <summary>The title, such as Halloween.</summary>
    public required string Title { get; set; }

    /// <summary>The year it came out, such as 1978. Null when unknown, never 0 (DATA-3).</summary>
    public int? Year { get; set; }

    /// <summary>How long it runs, in minutes. Null when unknown, never 0 (DATA-3).</summary>
    public int? RuntimeMinutes { get; set; }

    /// <summary>The US rating, such as PG-13. Null when unknown, never "NR" or blank (DATA-3).</summary>
    public string? Rating { get; set; }

    /// <summary>The director's name. Null when unknown.</summary>
    public string? Director { get; set; }

    /// <summary>TMDB's summary of the story. Null when unknown.</summary>
    public string? Overview { get; set; }

    /// <summary>
    /// The poster's file name, such as 948.jpg, made from the TMDB ID and never the title (SEC-5).
    /// Null when the movie has no poster.
    /// </summary>
    public string? PosterFile { get; set; }

    /// <summary>
    /// When TMDB's details were last fetched, in UTC (DATA-8). TMDB's terms cap caching at six
    /// months, so the refresh fetches them again after five. Null for a movie entered by hand.
    /// </summary>
    public DateTime? TmdbFetchedAt { get; set; }

    /// <summary>The top-billed cast, from TMDB.</summary>
    public ICollection<CastMember> Cast { get; } = [];

    /// <summary>The movie's genres, from TMDB.</summary>
    public ICollection<Genre> Genres { get; } = [];

    /// <summary>The cases that hold this movie.</summary>
    public ICollection<Case> Cases { get; } = [];
}
