using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rewind.Core.Catalog;
using Rewind.Core.Data;
using Rewind.Core.Metadata;
using Rewind.Core.Scans;

namespace Rewind.Core.Intake;

/// <summary>
/// Finds movies on TMDB for the Intake screen, gets each movie the clerk picks or types in ready
/// for the case being entered, and saves the case with its movies when its store label is scanned
/// (Build Plan 2.3 and 2.4, ADR-0005). Each TMDB movie is in the collection once: a second edition
/// joins the one already there.
/// </summary>
public sealed partial class MovieIntake(
    TmdbClient tmdb,
    Posters posters,
    IDbContextFactory<RewindDb> dbFactory,
    TimeProvider time,
    ILogger<MovieIntake> logger) : IMovieIntake
{
    // The earliest year a movie typed in by hand can have: Roundhay Garden Scene's, the oldest film
    // that survives. An earlier year is a slip.
    private const int FirstFilmYear = 1888;

    // The longest runtime a movie typed in by hand can have: ten hours, longer than any movie on a
    // DVD, so a slip such as 1100 for 110 is caught.
    private const int MaxRuntimeMinutes = 600;

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
    public async Task<MoviePick> PickAsync(int tmdbId, CancellationToken cancellationToken)
    {
        if (await OwnedAsync(tmdbId, cancellationToken) is { } owned)
        {
            LogAlreadyOwned(logger, tmdbId, owned.MovieId);
            return new MoviePick(PickStatus.Owned, owned);
        }

        var details = await tmdb.GetMovieAsync(tmdbId, cancellationToken);
        if (details.Movie is not { } found)
        {
            return new MoviePick(PickStatusFor(details.Status), null);
        }

        var fetchedAt = time.GetUtcNow().UtcDateTime; // NET-8, DATA-8
        var held = await DownloadPostersAsync(found, cancellationToken);
        return new MoviePick(PickStatus.Fetched, new CaseMovieEntry.Fetched(found, held, fetchedAt));
    }

    /// <inheritdoc />
    public HandEntry EnterByHand(string title, string? year, string? runtimeMinutes, string? rating)
    {
        ArgumentNullException.ThrowIfNull(title);

        // Cleaned as TMDB's titles are, so a typed one is kept like any other, and is safe in a
        // line of the log (SEC-5).
        if (WebAnswers.Clean(title, TmdbClient.MaxNameLength) is not { } cleanTitle)
        {
            return new HandEntry(HandEntryStatus.NoTitle, null);
        }

        var nextYear = time.GetLocalNow().Year + 1; // NET-8: next year on the register's own calendar
        if (!TryReadWholeNumber(year, FirstFilmYear, nextYear, out var knownYear))
        {
            return new HandEntry(HandEntryStatus.NotAYear, null);
        }

        if (!TryReadWholeNumber(runtimeMinutes, 1, MaxRuntimeMinutes, out var knownRuntime))
        {
            return new HandEntry(HandEntryStatus.NotARuntime, null);
        }

        var knownRating = UsRatings.Find(rating);
        if (knownRating is null && !string.IsNullOrWhiteSpace(rating))
        {
            return new HandEntry(HandEntryStatus.NotARating, null);
        }

        return new HandEntry(
            HandEntryStatus.Entered,
            new CaseMovieEntry.ByHand(cleanTitle, knownYear, knownRuntime, knownRating));
    }

    /// <inheritdoc />
    public async Task<Shelving> ShelveAsync(
        string storeLabel,
        string? factoryUpc,
        IReadOnlyList<CaseMovieEntry> movies,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storeLabel);
        ArgumentNullException.ThrowIfNull(movies);
        if (Refused(storeLabel, factoryUpc, movies, out var label, out var upc) is { } refused)
        {
            return new Shelving(refused, null);
        }

        try
        {
            return await SaveCaseAsync(label, upc, Once(movies), cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteExtendedErrorCode: SQLitePCL.raw.SQLITE_CONSTRAINT_UNIQUE })
        {
            // DATA-11: the label was free when SaveCaseAsync looked, and another case took it
            // before the save. The unique index is the real guard. Anything else it refused would
            // be a bug, such as a movie that's in the collection already being added again.
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (await LabelTakenAsync(db, label, cancellationToken))
            {
                LogLabelInUse(logger, label);
                return new Shelving(ShelveStatus.LabelInUse, null);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> CountOnShelfAsync(CancellationToken cancellationToken)
    {
        // Cases, not CaseMovies: a box set is one case on the shelf. A count reads no rows into
        // entities, so there's nothing to track (DATA-12).
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Cases.CountAsync(c => c.Status == CaseStatus.OnShelf, cancellationToken);
    }

    // Why a case can't be saved, or null when it can. SEC-5: the label and the barcode are read as
    // a scan is, and come back in Rewind's form, which the Cases table's CHECKs hold them to.
    private static ShelveStatus? Refused(
        string storeLabel,
        string? factoryUpc,
        IReadOnlyList<CaseMovieEntry> movies,
        out string label,
        out string? upc)
    {
        label = "";
        upc = null;
        if (Barcodes.Classify(storeLabel) is not { Kind: ScanKind.StoreLabel, Code: var code })
        {
            return ShelveStatus.NotALabel;
        }

        label = code;
        if (factoryUpc is not null && !Upc.TryNormalize(factoryUpc, out upc))
        {
            return ShelveStatus.NotAUpc;
        }

        return movies.Count == 0 ? ShelveStatus.NoMovies : null;
    }

    // DATA-7: the case, its new movies with their cast and genres, and its links to every movie, in
    // one SaveChanges, so a save that fails leaves nothing behind, not even a movie (ADR-0005). The
    // posters come after it, since a poster whose movie failed to save would never be cleaned up.
    private async Task<Shelving> SaveCaseAsync(
        string label,
        string? upc,
        IReadOnlyList<CaseMovieEntry> movies,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        // NET-12: a label that's in use is a slip of the clerk's, not a failed save, so it's found
        // before saving. Left to the index, every one would be an error in the log, from EF Core.
        if (await LabelTakenAsync(db, label, cancellationToken))
        {
            LogLabelInUse(logger, label);
            return new Shelving(ShelveStatus.LabelInUse, null);
        }

        var shelved = new Case { StoreLabel = label, FactoryUpc = upc, Status = CaseStatus.OnShelf };
        foreach (var movie in await MoviesForAsync(db, movies, cancellationToken))
        {
            shelved.Movies.Add(movie);
        }

        db.Cases.Add(shelved);
        await db.SaveChangesAsync(cancellationToken);
        if (logger.IsEnabled(LogLevel.Information))
        {
            var titles = string.Join(", ", movies.Select(TitleAndYear));
            LogShelved(logger, label, titles);
        }

        // NET-7's exception for a committed write: the case is on the shelf now, so its posters are
        // saved even if the clerk has just left the screen, and the token isn't passed along.
        // Cancelled from here, a saved case would come back as an exception, and its label as in
        // use at the next scan (NET-12). A few hundred KB to the register's own disk isn't worth
        // stopping for.
        foreach (var fetched in movies.OfType<CaseMovieEntry.Fetched>())
        {
            await SavePostersAsync(fetched.Details.TmdbId, fetched.PostersBySize, CancellationToken.None);
        }

        return new Shelving(
            ShelveStatus.Shelved,
            new ShelvedCase(label, [.. movies.Select(movie => new ShelvedMovie(movie.Title, movie.Year))]));
    }

    // The case's movies as the database holds them: one it has already, loaded so the case links
    // to it, or a new one, made from TMDB's details or from what the clerk typed.
    private static async Task<List<Movie>> MoviesForAsync(
        RewindDb db,
        IReadOnlyList<CaseMovieEntry> movies,
        CancellationToken cancellationToken)
    {
        var ownedIds = movies.OfType<CaseMovieEntry.Owned>().Select(owned => owned.MovieId).ToList();
        var owned = await db.Movies
            .Where(m => ownedIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, cancellationToken);
        var genres = await KnownGenresAsync(db, movies.OfType<CaseMovieEntry.Fetched>().Select(f => f.Details), cancellationToken);
        return [.. movies.Select(movie => movie switch
        {
            CaseMovieEntry.Owned o => owned[o.MovieId],
            CaseMovieEntry.Fetched f => ToMovie(f.Details, f.FetchedAt, genres),
            CaseMovieEntry.ByHand h => ByHandMovie(h),
            _ => throw new ArgumentException($"Rewind can't shelve a {movie.GetType().Name}.", nameof(movies)),
        })];
    }

    // A TMDB movie picked twice for one case is one movie in it: Movies.TmdbId's unique index and
    // the case's links would refuse it twice. Two movies typed in by hand are two movies, even with
    // one title, since nothing says they're the same.
    private static List<CaseMovieEntry> Once(IReadOnlyList<CaseMovieEntry> movies)
    {
        var picked = new HashSet<int>();
        return [.. movies.Where(movie => movie.TmdbId is not { } tmdbId || picked.Add(tmdbId))];
    }

    private static Task<bool> LabelTakenAsync(RewindDb db, string label, CancellationToken cancellationToken) =>
        db.Cases.AnyAsync(c => c.StoreLabel == label, cancellationToken);

    // DATA-12: read untracked, straight into the entry the screen lists, with the poster from its
    // file, so the list shows what was saved. One query, said out loud, or EF Core logs a warning
    // about loading two collections at once: with five actors and a few genres, the rows its joins
    // repeat are few.
    private async Task<CaseMovieEntry.Owned?> OwnedAsync(int tmdbId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var owned = await db.Movies
            .AsNoTracking()
            .AsSingleQuery()
            .Where(m => m.TmdbId == tmdbId)
            .Select(m => new CaseMovieEntry.Owned(
                m.Id,
                tmdbId,
                m.Title,
                m.Year,
                m.RuntimeMinutes,
                m.Rating,
                m.Director,
                m.Cast.OrderBy(c => c.BillingOrder).Select(c => c.Name).ToList(),
                m.Genres.OrderBy(g => g.Name).Select(g => g.Name).ToList(),
                null))
            .SingleOrDefaultAsync(cancellationToken);
        return owned is null
            ? null
            : owned with { Poster = await posters.ReadAsync(tmdbId, PosterSize.Register, cancellationToken) };
    }

    // Both sizes at once, held in memory until they're saved (ADR-0005). A size that won't download
    // is left out, so the movie goes without that file for now (GEN-7).
    private async Task<IReadOnlyDictionary<PosterSize, byte[]>> DownloadPostersAsync(TmdbMovie movie, CancellationToken cancellationToken)
    {
        var held = new Dictionary<PosterSize, byte[]>();
        if (movie.PosterPath is not { } path)
        {
            return held;
        }

        var downloads = await Task.WhenAll(SavedSizes.Select(async size =>
            (Size: size, Jpeg: await tmdb.GetPosterAsync(path, size, cancellationToken))));
        foreach (var (size, jpeg) in downloads)
        {
            if (jpeg is null)
            {
                LogPosterMissing(logger, size, movie.TmdbId);
                continue;
            }

            held.Add(size, jpeg);
        }

        return held;
    }

    // A poster that can't be saved leaves the movie without its file for now (GEN-7). PosterFile
    // still names it, so the TMDB refresh (Build Plan 4.5) can download it, as it will every poster
    // that's missing after a restore. A save fails when the data folder's permissions are wrong, or
    // when antivirus is still reading the new file as it's renamed.
    private async Task SavePostersAsync(int tmdbId, IReadOnlyDictionary<PosterSize, byte[]> held, CancellationToken cancellationToken)
    {
        foreach (var (size, jpeg) in held)
        {
            try
            {
                await posters.SaveAsync(tmdbId, size, jpeg, cancellationToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                LogPosterNotSaved(logger, e, size, tmdbId);
            }
        }
    }

    // Genres are shared, and Genres.TmdbId is unique, so a genre the database has already is
    // linked rather than added again. ToMovie adds each new one here as it makes it, so new movies
    // saved together that share a new genre share one row.
    private static async Task<Dictionary<int, Genre>> KnownGenresAsync(
        RewindDb db,
        IEnumerable<TmdbMovie> movies,
        CancellationToken cancellationToken)
    {
        var genreIds = movies.SelectMany(movie => movie.Genres).Select(genre => genre.TmdbId).Distinct().ToList();
        return await db.Genres
            .Where(g => genreIds.Contains(g.TmdbId))
            .ToDictionaryAsync(g => g.TmdbId, cancellationToken);
    }

    private static Movie ToMovie(TmdbMovie found, DateTime fetchedAt, Dictionary<int, Genre> genres)
    {
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
            TmdbFetchedAt = fetchedAt,
        };
        for (var place = 0; place < found.Cast.Count; place++)
        {
            movie.Cast.Add(new CastMember { Name = found.Cast[place], BillingOrder = place });
        }

        foreach (var genre in found.Genres)
        {
            if (!genres.TryGetValue(genre.TmdbId, out var known))
            {
                known = new Genre { TmdbId = genre.TmdbId, Name = genre.Name };
                genres.Add(genre.TmdbId, known);
            }

            movie.Genres.Add(known);
        }

        return movie;
    }

    // A movie TMDB doesn't have: no TMDB ID, fetch time or poster file, and no cast or genres,
    // which come only from TMDB (DATA-9). Whatever the clerk left blank stays NULL (DATA-3).
    private static Movie ByHandMovie(CaseMovieEntry.ByHand movie) => new()
    {
        Title = movie.Title,
        Year = movie.Year,
        RuntimeMinutes = movie.RuntimeMinutes,
        Rating = movie.Rating,
    };

    // A number the clerk typed, within its bounds. Blank is unknown, so null (DATA-3). NumberStyles.None
    // takes the digits 0 to 9 alone: no sign, separator or decimal point, and no other script's digits.
    private static bool TryReadWholeNumber(string? text, int lowest, int highest, out int? number)
    {
        number = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || value < lowest
            || value > highest)
        {
            return false;
        }

        number = value;
        return true;
    }

    // Such as "Halloween (1978)", for the log.
    private static string TitleAndYear(CaseMovieEntry movie) =>
        movie.Year is { } year ? string.Create(CultureInfo.InvariantCulture, $"{movie.Title} ({year})") : movie.Title;

    private static PickStatus PickStatusFor(TmdbStatus status) => status switch
    {
        TmdbStatus.NotSetUp => PickStatus.NotSetUp,
        TmdbStatus.TryLater => PickStatus.TryLater,
        _ => PickStatus.NotFound,
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Put case {StoreLabel} on the shelf, holding {Movies}")]
    private static partial void LogShelved(ILogger logger, string storeLabel, string movies);

    [LoggerMessage(Level = LogLevel.Debug, Message = "TMDB movie {TmdbId} is already in the collection, as movie {MovieId}")]
    private static partial void LogAlreadyOwned(ILogger logger, int tmdbId, int movieId);

    // Information, not Debug (OBS-3): a label on two cases is worth finding in the log a month on,
    // such as when a sheet of labels was printed twice (Build Plan 2.5).
    [LoggerMessage(Level = LogLevel.Information, Message = "Store label {StoreLabel} is on another case already, so nothing was saved")]
    private static partial void LogLabelInUse(ILogger logger, string storeLabel);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't download the {Size} poster for TMDB movie {TmdbId}; the movie goes without it for now")]
    private static partial void LogPosterMissing(ILogger logger, PosterSize size, int tmdbId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't save the {Size} poster for TMDB movie {TmdbId}; the movie goes without it for now")]
    private static partial void LogPosterNotSaved(ILogger logger, Exception exception, PosterSize size, int tmdbId);
}
