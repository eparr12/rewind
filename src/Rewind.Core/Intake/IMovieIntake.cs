using Rewind.Core.Catalog;
using Rewind.Core.Metadata;

namespace Rewind.Core.Intake;

/// <summary>
/// What the Intake screen asks of Core, one call per action (ARCH-5): find a movie on TMDB, pick one
/// of its matches or type a movie in by hand for the case being entered, then save the case with
/// its movies when its store label is scanned (ADR-0005). <see cref="MovieIntake"/> does it, and
/// the screen's tests use a fake (ADR-0003).
/// </summary>
public interface IMovieIntake
{
    /// <summary>
    /// Searches TMDB by title, and downloads each match's poster at the picker's size. A match
    /// whose poster can't be downloaded still shows, without one.
    /// </summary>
    Task<TmdbSearch> SearchAsync(string title, CancellationToken cancellationToken);

    /// <summary>
    /// Adds the movie the clerk picked to the collection, with its details, cast, genres and
    /// posters, all fetched from TMDB. A movie that's already in the collection is left as it is.
    /// This is Build Plan 2.3's way, which saves at the pick. The Intake screen moves to
    /// <see cref="PickAsync"/> and <see cref="ShelveAsync"/> next, and this goes once nothing calls
    /// it (ADR-0005).
    /// </summary>
    /// <param name="tmdbId">The picked movie's TMDB ID, from its <see cref="TmdbMatch"/>.</param>
    /// <param name="cancellationToken">Cancels the add. Nothing is saved until TMDB has answered.</param>
    Task<MovieAdd> AddAsync(int tmdbId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the movie the clerk picked ready to go in the case, and saves nothing (ADR-0005). A
    /// movie the collection already has is read from the database, without asking TMDB, so the case
    /// joins it. Any other is fetched from TMDB with its details and its posters at both saved
    /// sizes, and held until <see cref="ShelveAsync"/> saves it. A poster that won't download leaves
    /// the movie without it (GEN-7).
    /// </summary>
    /// <param name="tmdbId">The picked movie's TMDB ID, from its <see cref="TmdbMatch"/>.</param>
    /// <param name="cancellationToken">Cancels the pick, such as when the clerk leaves the screen.</param>
    Task<MoviePick> PickAsync(int tmdbId, CancellationToken cancellationToken);

    /// <summary>
    /// Checks a movie the clerk typed in by hand, for a DVD TMDB doesn't have, and gets it ready to
    /// go in the case. Nothing is saved, and TMDB isn't asked. The title is cleaned as TMDB's are:
    /// trimmed, and capped at <see cref="TmdbClient.MaxNameLength"/> characters (SEC-5). A year,
    /// runtime or rating left blank is unknown, so null (DATA-3).
    /// </summary>
    /// <param name="title">The title, which can't be blank.</param>
    /// <param name="year">The year it came out, in digits, from 1888 to next year, or blank.</param>
    /// <param name="runtimeMinutes">How long it runs, in whole minutes from 1 to 600, or blank.</param>
    /// <param name="rating">One of <see cref="UsRatings.All"/>, in any letter case, or blank.</param>
    HandEntry EnterByHand(string title, string? year, string? runtimeMinutes, string? rating);

    /// <summary>
    /// Saves the case whose store label the clerk just scanned, on the shelf, with its movies
    /// (ADR-0005). The case, the new movies with their cast and genres, and the case's links to
    /// every movie go in one save (DATA-7), so a save that fails leaves nothing behind. The new
    /// movies' posters are saved after that; one that can't be leaves its movie without it for now
    /// (GEN-7). A movie picked twice is saved once. A label another case has is refused, and
    /// nothing is saved.
    /// </summary>
    /// <param name="storeLabel">The label as scanned, such as RV-00042.</param>
    /// <param name="factoryUpc">
    /// The case's factory barcode, such as 032429258373, as <see cref="BarcodeTitle.Upc"/> gives it.
    /// Null when the case has none.
    /// </param>
    /// <param name="movies">
    /// The case's movies, from <see cref="PickAsync"/> and <see cref="EnterByHand"/>, in the order
    /// they were added.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the save until it's committed, such as when the clerk leaves the screen. Once the
    /// case is saved, its posters are saved too, whatever the token says, and the case comes back
    /// <see cref="ShelveStatus.Shelved"/>, so a case on the shelf is never reported as cancelled.
    /// </param>
    Task<Shelving> ShelveAsync(
        string storeLabel,
        string? factoryUpc,
        IReadOnlyList<CaseMovieEntry> movies,
        CancellationToken cancellationToken);
}
