using Rewind.Core.Metadata;

namespace Rewind.Core.Intake;

/// <summary>
/// One movie in the case the clerk is entering on the Intake screen. Nothing is saved while the
/// case is open: the screen holds its movies until the case's store label is scanned, and
/// <see cref="IMovieIntake.ShelveAsync"/> saves them with the case (ADR-0005). Each entry has what
/// the screen lists, and its kind says how the case gets the movie: <see cref="Owned"/>,
/// <see cref="Fetched"/> or <see cref="ByHand"/>. There's no overview: the clerk read the story in
/// the picker, where they chose by it, and the case's list leaves it out so that a box set's list
/// stays short. A <see cref="Fetched"/> entry still has it in its details, and saves it.
/// </summary>
/// <param name="TmdbId">
/// The movie's ID at TMDB, such as 948 for Halloween (1978), which tells a movie picked twice from
/// two movies. Null for a movie entered by hand.
/// </param>
/// <param name="Title">The title, such as Halloween.</param>
/// <param name="Year">The year it came out. Null when unknown (DATA-3).</param>
/// <param name="RuntimeMinutes">How long it runs, in minutes. Null when unknown.</param>
/// <param name="Rating">The US rating, such as R. Null when unknown.</param>
/// <param name="Director">The director. Null when unknown.</param>
/// <param name="Cast">The top-billed actors, top of the bill first.</param>
/// <param name="Genres">The genres, in alphabetical order.</param>
/// <param name="Poster">
/// The poster at the register's size. Null when the movie has none, or when it couldn't be
/// downloaded or read.
/// </param>
public abstract record CaseMovieEntry(
    int? TmdbId,
    string Title,
    int? Year,
    int? RuntimeMinutes,
    string? Rating,
    string? Director,
    IReadOnlyList<string> Cast,
    IReadOnlyList<string> Genres,
    byte[]? Poster)
{
    /// <summary>
    /// A movie the collection already has, so the case joins it instead of adding it again, as a
    /// second edition does (Overview: intake). What the screen lists is read from the database, and
    /// the poster from its file.
    /// </summary>
    /// <param name="MovieId">The movie's database key.</param>
    public sealed record Owned(
        int MovieId,
        int? TmdbId,
        string Title,
        int? Year,
        int? RuntimeMinutes,
        string? Rating,
        string? Director,
        IReadOnlyList<string> Cast,
        IReadOnlyList<string> Genres,
        byte[]? Poster)
        : CaseMovieEntry(TmdbId, Title, Year, RuntimeMinutes, Rating, Director, Cast, Genres, Poster);

    /// <summary>
    /// A movie new to the collection, fetched from TMDB with its posters and held until the label
    /// scan saves it, with its cast and genres, then its posters.
    /// </summary>
    /// <param name="Details">TMDB's details, as the movie is saved.</param>
    /// <param name="PostersBySize">
    /// The posters at the sizes Rewind saves, <see cref="PosterSize.Register"/> and
    /// <see cref="PosterSize.Thumb"/>, as TMDB sent them. A size that couldn't be downloaded is
    /// missing, and the movie is saved without it for now (GEN-7).
    /// </param>
    /// <param name="FetchedAt">
    /// When TMDB's details were fetched, in UTC, kept as the movie's TmdbFetchedAt (DATA-8). TMDB's
    /// terms count the six months Rewind may keep them from here.
    /// </param>
    public sealed record Fetched(TmdbMovie Details, IReadOnlyDictionary<PosterSize, byte[]> PostersBySize, DateTime FetchedAt)
        : CaseMovieEntry(
            Details.TmdbId,
            Details.Title,
            Details.Year,
            Details.RuntimeMinutes,
            Details.Rating,
            Details.Director,
            Details.Cast,
            [.. Details.Genres.Select(genre => genre.Name).Order(StringComparer.Ordinal)],
            PostersBySize.GetValueOrDefault(PosterSize.Register));

    /// <summary>
    /// A movie TMDB doesn't have, typed in by the clerk as <see cref="IMovieIntake.EnterByHand"/>
    /// checked it. It has no TMDB ID, director, cast, genres or poster.
    /// </summary>
    public sealed record ByHand(string Title, int? Year, int? RuntimeMinutes, string? Rating)
        : CaseMovieEntry(null, Title, Year, RuntimeMinutes, Rating, null, [], [], null);
}
