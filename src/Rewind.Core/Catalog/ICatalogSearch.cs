namespace Rewind.Core.Catalog;

/// <summary>
/// What F3 Ask the Clerk asks of Core to search the catalog, one call per thing the clerk does (ARCH-5): the genres for
/// the screen's list, and each search. Both only read, and both look only at the listed movies: each one with at least
/// one case that isn't retired, as the phone catalog lists them. <see cref="CatalogSearch"/> does it, and the screen's
/// tests will use a fake (ADR-0003).
/// </summary>
public interface ICatalogSearch
{
    /// <summary>
    /// The genres for the screen's list: the name of each genre a listed movie has, A to Z as the other lists sort. A
    /// name two genres share, which only their TMDB IDs keep apart, is listed once, and a genre that only movies that
    /// aren't listed have is left out. Two names that differ only in letter case go in order by character code, so the
    /// list always reads the same.
    /// </summary>
    Task<IReadOnlyList<string>> GenresAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Searches the listed movies for what the clerk typed and picked, as they typed and picked it. The text, the genre,
    /// the year and the rating each narrow the search, alone or together, and nothing typed or picked lists the whole
    /// catalog. A filter never matches an unknown (DATA-3): a movie with no year isn't from 1978, one with no rating
    /// isn't rated R, and one with no genres has none of them. A year or a rating that isn't one comes back as that
    /// status (NET-12), and the database isn't asked.
    /// </summary>
    /// <remarks>
    /// The hits go by title, A to Z as the other lists sort, then by year, unknown first, then by Id, so the same search
    /// always reads the same. A movie in two cases is one hit. The answer holds the first
    /// <see cref="CatalogSearch.MaxHits"/>, and says when more matched. The text is matched by SQLite's LIKE, which
    /// ignores letter case for A to Z only and doesn't fold accents, so neither amelie nor AMÉLIE finds Amélie (#100).
    /// </remarks>
    /// <param name="text">
    /// What the clerk typed. It matches a movie whose title, director or cast member's name has it anywhere, such as
    /// curtis in Jamie Lee Curtis. It's cleaned and capped as a title typed in by hand is (SEC-5), and a %, an _ or a
    /// backslash in it is matched as itself, not as a wildcard (DATA-13). Null, blank or only spaces matches every movie.
    /// </param>
    /// <param name="genre">A genre's name as <see cref="GenresAsync"/> gives it, or null or blank for any genre.</param>
    /// <param name="year">The year, in digits, as a movie typed in by hand takes one, or null or blank for any year.</param>
    /// <param name="rating">One of <see cref="UsRatings.All"/>, in any letter case, or null or blank for any rating.</param>
    /// <param name="cancellationToken">Cancels the search, such as when the clerk leaves the screen. The search then throws (NET-7).</param>
    Task<SearchResult> SearchAsync(string? text, string? genre, string? year, string? rating, CancellationToken cancellationToken);
}
