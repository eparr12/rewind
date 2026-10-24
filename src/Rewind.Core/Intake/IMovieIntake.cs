using Rewind.Core.Metadata;

namespace Rewind.Core.Intake;

/// <summary>
/// What the Intake screen asks of Core, one call per action (ARCH-5): find a movie on TMDB, then
/// add the clerk's pick to the collection. <see cref="MovieIntake"/> does it, and the screen's
/// tests use a fake (ADR-0003).
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
    /// </summary>
    /// <param name="tmdbId">The picked movie's TMDB ID, from its <see cref="TmdbMatch"/>.</param>
    /// <param name="cancellationToken">Cancels the add. Nothing is saved until TMDB has answered.</param>
    Task<MovieAdd> AddAsync(int tmdbId, CancellationToken cancellationToken);
}
