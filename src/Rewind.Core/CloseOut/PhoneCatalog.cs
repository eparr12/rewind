using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rewind.Core.Catalog;
using Rewind.Core.Data;
using Rewind.Core.Metadata;

namespace Rewind.Core.CloseOut;

/// <summary>
/// Writes the phone catalog to the outbox at Close Out (Build Plan 3.5): each movie's thumbnail, then
/// catalog.json, in the format catalog-json.md beside this file sets out (DOC-7, ARCH-8). The outbox holds
/// what the NAS's catalog folder will, and nothing uploads it until Build Plan 7.2. The catalog holds movies
/// only, never anything about the family (SEC-2).
/// </summary>
public sealed partial class PhoneCatalog(
    IDbContextFactory<RewindDb> dbFactory,
    IOptions<RewindPaths> options,
    TimeProvider time,
    ILogger<PhoneCatalog> logger)
{
    // catalog-json.md says when it changes. Adding a field never does.
    private const int FormatVersion = 1;

    private const string CatalogFileName = "catalog.json";
    private const string ThumbsFolder = "thumbs";

    // UTC to the second, ending in Z, such as 2026-10-24T02:15:00Z.
    private const string GeneratedAtFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    // camelCase, as catalog-json.md names the fields. Nulls are written, not left out, since a field that's
    // null says the value is unknown (DATA-3).
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly RewindPaths _paths = options.Value;

    /// <summary>
    /// Writes the outbox from the collection as it is now, and returns how many movies catalog.json lists. A
    /// movie is listed when at least one of its cases isn't retired, so one that's checked out still counts,
    /// with the factory UPCs of those cases. Each movie's thumbnail is copied from the data folder first, and
    /// catalog.json is written last, so it never names a thumbnail that isn't there. Each file is written
    /// whole under another name and then moved into place, so a write that fails or is cancelled leaves the
    /// last good catalog.json as it was. Nothing in the outbox is ever deleted. A file that can't be read or
    /// written throws, and so does a cancel.
    /// </summary>
    public async Task<int> WriteAsync(CancellationToken cancellationToken)
    {
        var generatedAt = time.GetUtcNow(); // NET-8
        var movies = await ReadMoviesAsync(cancellationToken);
        var thumbs = Path.Combine(_paths.Outbox, ThumbsFolder);
        Directory.CreateDirectory(thumbs);

        // Thumbnails first and catalog.json last, the order Build Plan 7.2 uploads them in. A movie entered by
        // hand has no TMDB ID, and so no thumbnail.
        foreach (var tmdbId in movies.Select(movie => movie.TmdbId).OfType<int>())
        {
            await CopyThumbAsync(tmdbId, thumbs, cancellationToken);
        }

        var catalog = new CatalogFile(
            FormatVersion,
            generatedAt.ToString(GeneratedAtFormat, CultureInfo.InvariantCulture),
            [.. movies.Select(movie => ToListed(movie, thumbs))]);
        await WholeFile.WriteAsync(Path.Combine(_paths.Outbox, CatalogFileName), JsonSerializer.SerializeToUtf8Bytes(catalog, Json), cancellationToken);
        LogWritten(logger, catalog.Movies.Count);
        return catalog.Movies.Count;
    }

    // DATA-12. The catalog answers "do we already own this?", so a movie whose only case is retired isn't in
    // it, and neither is a retired case's UPC. Movies and genres sort as SQLite compares text, by character
    // code, and the Id settles two movies with one title and year, so the same collection always reads in the
    // same order. Each case's UPC comes back, so two copies of one edition give it twice: a DISTINCT in here
    // needs an APPLY, which SQLite can't run. One statement, so the read sees the collection at one moment,
    // and saying so keeps EF Core's warning about two collections in one query out of the log. A movie's
    // genres times its UPCs is a handful of rows.
    private async Task<List<MovieRow>> ReadMoviesAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Movies
            .AsNoTracking()
            .Where(m => m.Cases.Any(c => c.Status != CaseStatus.Retired))
            .OrderBy(m => m.Title)
            .ThenBy(m => m.Year)
            .ThenBy(m => m.Id)
            .Select(m => new MovieRow(
                m.TmdbId,
                m.Title,
                m.Year,
                m.Rating,
                m.RuntimeMinutes,
                m.Genres.Select(g => g.Name).OrderBy(name => name).ToList(),
                m.Cases
                    .Where(c => c.Status != CaseStatus.Retired && c.FactoryUpc != null)
                    .Select(c => c.FactoryUpc!) // NET-1: the Where above leaves out the cases with none
                    .ToList()))
            .AsSingleQuery()
            .ToListAsync(cancellationToken);
    }

    // Copied when the outbox has no thumbnail for the movie, or has one whose bytes differ from the register's.
    // Both are read whole: a thumbnail is a few kilobytes.
    private async Task CopyThumbAsync(int tmdbId, string thumbs, CancellationToken cancellationToken)
    {
        var fileName = Posters.FileNameFor(tmdbId); // SEC-5: named for the TMDB ID, never the title
        var saved = Path.Combine(_paths.Thumbs, fileName);
        if (!File.Exists(saved))
        {
            return; // never downloaded, or not yet downloaded again after a restore
        }

        var jpeg = await File.ReadAllBytesAsync(saved, cancellationToken);
        var copy = Path.Combine(thumbs, fileName);
        if (File.Exists(copy) && (await File.ReadAllBytesAsync(copy, cancellationToken)).AsSpan().SequenceEqual(jpeg))
        {
            return; // the same one, so it isn't written again
        }

        await WholeFile.WriteAsync(copy, jpeg, cancellationToken);
    }

    // A movie names its thumbnail when the outbox holds it, even when the data folder no longer does, such as
    // after a restore, before the posters download again. Each UPC is listed once, in order by character code,
    // as SQLite sorts the titles.
    private static ListedMovie ToListed(MovieRow movie, string thumbs)
    {
        var fileName = movie.TmdbId is { } tmdbId ? Posters.FileNameFor(tmdbId) : null;
        var thumb = fileName is not null && File.Exists(Path.Combine(thumbs, fileName))
            ? $"{ThumbsFolder}/{fileName}" // a web path, so a forward slash on every OS
            : null;
        var upcs = new SortedSet<string>(movie.CaseUpcs, StringComparer.Ordinal);
        return new ListedMovie(movie.Title, movie.Year, movie.Rating, movie.Minutes, movie.Genres, thumb, [.. upcs]);
    }

    // Information (OBS-3): a Close Out step, and how many movies the phones will know.
    [LoggerMessage(Level = LogLevel.Information, Message = "Wrote the phone catalog to the outbox: movies {Movies}")]
    private static partial void LogWritten(ILogger logger, int movies);

    // What the read gives each movie: the TMDB ID that names its thumbnail, what catalog.json lists, and the
    // UPC of each of its cases that has one and isn't retired, repeats and all.
    private sealed record MovieRow(int? TmdbId, string Title, int? Year, string? Rating, int? Minutes, List<string> Genres, List<string> CaseUpcs);

    // catalog.json, field for field, in catalog-json.md's order.
    private sealed record CatalogFile(int Version, string GeneratedAt, IReadOnlyList<ListedMovie> Movies);

    private sealed record ListedMovie(string Title, int? Year, string? Rating, int? Minutes, IReadOnlyList<string> Genres, string? Thumb, IReadOnlyList<string> Upcs);
}
