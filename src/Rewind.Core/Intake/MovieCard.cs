namespace Rewind.Core.Intake;

/// <summary>A movie in the collection, as the Intake screen shows it once it's added.</summary>
/// <param name="MovieId">The movie's database key.</param>
/// <param name="Title">The title, such as Halloween.</param>
/// <param name="Year">The year it came out. Null when unknown (DATA-3).</param>
/// <param name="RuntimeMinutes">How long it runs, in minutes. Null when unknown.</param>
/// <param name="Rating">The US rating, such as R. Null when unknown.</param>
/// <param name="Director">The director. Null when unknown.</param>
/// <param name="Overview">TMDB's summary of the story. Null when unknown.</param>
/// <param name="Cast">The top-billed actors, top of the bill first.</param>
/// <param name="Genres">The genres, in alphabetical order.</param>
public sealed record MovieCard(
    int MovieId,
    string Title,
    int? Year,
    int? RuntimeMinutes,
    string? Rating,
    string? Director,
    string? Overview,
    IReadOnlyList<string> Cast,
    IReadOnlyList<string> Genres)
{
    /// <summary>
    /// The poster at the register's size. Null when the movie has none, or when its file is
    /// missing, such as when it couldn't be downloaded.
    /// </summary>
    public byte[]? Poster { get; init; }
}
