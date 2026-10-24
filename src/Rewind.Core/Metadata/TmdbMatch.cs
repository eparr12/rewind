namespace Rewind.Core.Metadata;

/// <summary>
/// One movie in TMDB's search results, with what the clerk needs to tell remakes apart: the year,
/// the overview and the poster (Build Plan 2.3).
/// </summary>
/// <param name="TmdbId">The movie's ID at TMDB, such as 948 for Halloween (1978).</param>
/// <param name="Title">The title, such as Halloween.</param>
/// <param name="Year">The year it came out. Null when TMDB doesn't know it (DATA-3).</param>
/// <param name="Overview">TMDB's summary of the story. Null when there's none.</param>
/// <param name="PosterPath">
/// Where TMDB keeps the poster, such as /wijlZ3HaYMvlDTPqJoTCWKFkCPU.jpg. Null when the movie has
/// none, or when the path isn't one Rewind trusts (SEC-5).
/// </param>
public sealed record TmdbMatch(int TmdbId, string Title, int? Year, string? Overview, string? PosterPath)
{
    /// <summary>
    /// The poster at the picker's size, once it's downloaded. Null when the movie has none, or
    /// when it couldn't be downloaded.
    /// </summary>
    public byte[]? Poster { get; init; }
}
