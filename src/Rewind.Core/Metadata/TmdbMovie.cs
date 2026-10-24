namespace Rewind.Core.Metadata;

/// <summary>
/// A movie's details from TMDB, as Rewind keeps them: anything unknown is null, never 0, blank or
/// "NR" (DATA-3), and text is trimmed and capped (SEC-5).
/// </summary>
/// <param name="TmdbId">The movie's ID at TMDB, such as 948 for Halloween (1978).</param>
/// <param name="Title">The title, such as Halloween.</param>
/// <param name="Year">The year it came out, from its release date.</param>
/// <param name="RuntimeMinutes">How long it runs, in minutes. TMDB's 0 means it doesn't know, so it's null.</param>
/// <param name="Rating">
/// The US rating, such as R: the first of the movie's US releases with G, PG, PG-13, R or NC-17.
/// Null when none has one, since TMDB's NR means "No rating information".
/// </param>
/// <param name="Director">The director, or the directors with commas between them.</param>
/// <param name="Overview">TMDB's summary of the story.</param>
/// <param name="PosterPath">Where TMDB keeps the poster, such as /wijlZ3HaYMvlDTPqJoTCWKFkCPU.jpg.</param>
/// <param name="Cast">The top-billed actors, top of the bill first.</param>
/// <param name="Genres">The movie's genres.</param>
public sealed record TmdbMovie(
    int TmdbId,
    string Title,
    int? Year,
    int? RuntimeMinutes,
    string? Rating,
    string? Director,
    string? Overview,
    string? PosterPath,
    IReadOnlyList<string> Cast,
    IReadOnlyList<TmdbGenre> Genres);
