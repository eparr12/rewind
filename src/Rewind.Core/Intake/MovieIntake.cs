using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rewind.Core.Catalog;
using Rewind.Core.Data;
using Rewind.Core.Metadata;

namespace Rewind.Core.Intake;

/// <summary>
/// Finds movies on TMDB for the Intake screen, and adds the clerk's pick to the collection (Build
/// Plan 2.3). Each TMDB movie is added once: picking it again finds the one already there.
/// </summary>
public sealed partial class MovieIntake(
    TmdbClient tmdb,
    Posters posters,
    IDbContextFactory<RewindDb> dbFactory,
    TimeProvider time,
    ILogger<MovieIntake> logger) : IMovieIntake
{
    // The sizes a movie's poster is saved at: the register's screens and the phone catalog's.
    private static readonly PosterSize[] SavedSizes = [PosterSize.Register, PosterSize.Thumb];

    /// <inheritdoc />
    public async Task<TmdbSearch> SearchAsync(string title, CancellationToken cancellationToken)
    {
        var search = await tmdb.SearchAsync(title, cancellationToken);

        // All at once, so a page of 20 posters takes about as long as one.
        var matches = await Task.WhenAll(search.Matches.Select(async match => match with
        {
            Poster = match.PosterPath is { } path
                ? await tmdb.GetPosterAsync(path, PosterSize.Picker, cancellationToken)
                : null,
        }));
        return search with { Matches = matches };
    }

    /// <inheritdoc />
    public async Task<MovieAdd> AddAsync(int tmdbId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await CardAsync(db, tmdbId, cancellationToken) is { } owned)
        {
            LogAlreadyOwned(logger, tmdbId, owned.MovieId);
            return new MovieAdd(AddStatus.AlreadyOwned, owned);
        }

        var details = await tmdb.GetMovieAsync(tmdbId, cancellationToken);
        if (details.Movie is not { } found)
        {
            return new MovieAdd(StatusFor(details.Status), null);
        }

        // The movie first, then its posters. A movie whose poster is missing is a case Rewind
        // handles already (see SavePostersAsync), but a poster whose movie failed to save would
        // never be cleaned up. A retry after a poster fails finds the movie already added.
        db.Movies.Add(await ToMovieAsync(db, found, cancellationToken));
        await db.SaveChangesAsync(cancellationToken); // DATA-7: the movie, its cast and its genres together
        LogAdded(logger, tmdbId, found.Title);
        await SavePostersAsync(found, cancellationToken);
        return new MovieAdd(AddStatus.Added, await CardAsync(db, tmdbId, cancellationToken));
    }

    // DATA-12: read untracked, straight into the record the screen shows. The poster comes from
    // its file, so the card shows what was saved. One query, said out loud, or EF Core logs a
    // warning about loading two collections at once: with five actors and a few genres, the rows
    // its joins repeat are few.
    private async Task<MovieCard?> CardAsync(RewindDb db, int tmdbId, CancellationToken cancellationToken)
    {
        var card = await db.Movies
            .AsNoTracking()
            .AsSingleQuery()
            .Where(m => m.TmdbId == tmdbId)
            .Select(m => new MovieCard(
                m.Id,
                m.Title,
                m.Year,
                m.RuntimeMinutes,
                m.Rating,
                m.Director,
                m.Overview,
                m.Cast.OrderBy(c => c.BillingOrder).Select(c => c.Name).ToList(),
                m.Genres.OrderBy(g => g.Name).Select(g => g.Name).ToList()))
            .SingleOrDefaultAsync(cancellationToken);
        return card is null
            ? null
            : card with { Poster = await posters.ReadAsync(tmdbId, PosterSize.Register, cancellationToken) };
    }

    // A poster that can't be downloaded or saved, or isn't because the clerk left the screen,
    // leaves the movie without its file for now (GEN-7). PosterFile still names it, so the TMDB
    // refresh (Build Plan 4.5) can download it, as it will every poster that's missing after a
    // restore. A save fails when the data folder's permissions are wrong, or when antivirus is
    // still reading the new file as it's renamed.
    private async Task SavePostersAsync(TmdbMovie movie, CancellationToken cancellationToken)
    {
        if (movie.PosterPath is not { } path)
        {
            return;
        }

        foreach (var size in SavedSizes)
        {
            if (await tmdb.GetPosterAsync(path, size, cancellationToken) is not { } poster)
            {
                LogPosterMissing(logger, size, movie.TmdbId);
                continue;
            }

            try
            {
                await posters.SaveAsync(movie.TmdbId, size, poster, cancellationToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                LogPosterNotSaved(logger, e, size, movie.TmdbId);
            }
        }
    }

    // Genres are shared, and Genres.TmdbId is unique, so a genre the database already has is
    // linked rather than added again.
    private async Task<Movie> ToMovieAsync(RewindDb db, TmdbMovie found, CancellationToken cancellationToken)
    {
        var genreIds = found.Genres.Select(g => g.TmdbId).ToList();
        var known = await db.Genres
            .Where(g => genreIds.Contains(g.TmdbId))
            .ToDictionaryAsync(g => g.TmdbId, cancellationToken);

        var movie = new Movie
        {
            TmdbId = found.TmdbId,
            Title = found.Title,
            Year = found.Year,
            RuntimeMinutes = found.RuntimeMinutes,
            Rating = found.Rating,
            Director = found.Director,
            Overview = found.Overview,
            PosterFile = found.PosterPath is null ? null : Posters.FileNameFor(found.TmdbId),
            TmdbFetchedAt = time.GetUtcNow().UtcDateTime, // NET-8, DATA-8
        };
        for (var place = 0; place < found.Cast.Count; place++)
        {
            movie.Cast.Add(new CastMember { Name = found.Cast[place], BillingOrder = place });
        }

        foreach (var genre in found.Genres)
        {
            movie.Genres.Add(known.GetValueOrDefault(genre.TmdbId) ?? new Genre { TmdbId = genre.TmdbId, Name = genre.Name });
        }

        return movie;
    }

    private static AddStatus StatusFor(TmdbStatus status) => status switch
    {
        TmdbStatus.NotSetUp => AddStatus.NotSetUp,
        TmdbStatus.TryLater => AddStatus.TryLater,
        _ => AddStatus.NotFound,
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Added TMDB movie {TmdbId}, {Title}, to the collection")]
    private static partial void LogAdded(ILogger logger, int tmdbId, string title);

    [LoggerMessage(Level = LogLevel.Debug, Message = "TMDB movie {TmdbId} is already in the collection, as movie {MovieId}")]
    private static partial void LogAlreadyOwned(ILogger logger, int tmdbId, int movieId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't download the {Size} poster for TMDB movie {TmdbId}; the movie is added without it")]
    private static partial void LogPosterMissing(ILogger logger, PosterSize size, int tmdbId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't save the {Size} poster for TMDB movie {TmdbId}; the movie is added without it")]
    private static partial void LogPosterNotSaved(ILogger logger, Exception exception, PosterSize size, int tmdbId);
}
