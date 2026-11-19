using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Rewind.Core.Data;
using Rewind.Core.Metadata;

namespace Rewind.Core.Catalog;

/// <summary>
/// Searches the catalog for F3 Ask the Clerk (Build Plan 4.1), and lists the genres its search can pick. Each call
/// reads with a context of its own and keeps nothing after it (DATA-6), so one serves every screen. A search isn't a
/// key event, so it logs nothing of its own (OBS-3), though <see cref="Posters"/> logs a thumbnail it can't read.
/// </summary>
public sealed class CatalogSearch(IDbContextFactory<RewindDb> dbFactory, Posters posters, TimeProvider time) : ICatalogSearch
{
    /// <summary>
    /// The most hits a search gives: 50, about what the clerk can read down a list. A search that matches more says so,
    /// so the clerk can narrow it.
    /// </summary>
    public const int MaxHits = 50;

    // SQLite's collation that folds A to Z, as the other lists sort.
    private const string NoCase = "NOCASE";

    // LIKE's wildcards: % matches any text, and _ any one character. The escape character before either of them, or
    // before itself, makes that character match itself.
    private const string AnyText = "%";
    private const string AnyCharacter = "_";
    private const string LikeEscape = @"\";

    // What's listed, as the phone catalog lists it: a movie with at least one case that isn't retired. Each case is
    // looked at in a subquery, never a join, so a movie in two cases is still one row.
    private static readonly Expression<Func<Movie, bool>> Listed = m => m.Cases.Any(c => c.Status != CaseStatus.Retired);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GenresAsync(CancellationToken cancellationToken)
    {
        // DATA-12: the names alone, untracked. Two genres can share a name, since only their TMDB IDs are unique, and
        // the list shows it once. NOCASE, as the other lists sort, then by character code, for two names that differ
        // only in letter case.
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Movies
            .AsNoTracking()
            .Where(Listed)
            .SelectMany(m => m.Genres.Select(g => g.Name))
            .Distinct()
            .OrderBy(name => EF.Functions.Collate(name, NoCase))
            .ThenBy(name => name)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SearchResult> SearchAsync(string? text, string? genre, string? year, string? rating, CancellationToken cancellationToken)
    {
        // Each checked before the database is asked, as EnterByHand checks a movie typed in by hand.
        if (!TypedNumbers.TryReadYear(year, time, out var knownYear))
        {
            return new SearchResult(SearchStatus.NotAYear, [], false);
        }

        var knownRating = UsRatings.Find(rating);
        if (knownRating is null && !string.IsNullOrWhiteSpace(rating))
        {
            return new SearchResult(SearchStatus.NotARating, [], false);
        }

        var pickedGenre = string.IsNullOrWhiteSpace(genre) ? null : genre;
        var rows = await ReadRowsAsync(LikePatternFor(text), pickedGenre, knownYear, knownRating, cancellationToken);
        var hits = new List<MovieHit>(Math.Min(rows.Count, MaxHits));
        foreach (var row in rows.Take(MaxHits))
        {
            var thumb = await ThumbAsync(row.TmdbId, cancellationToken);
            hits.Add(new MovieHit(row.MovieId, row.Title, row.Year, row.Rating, row.OnShelf, thumb));
        }

        return new SearchResult(SearchStatus.Searched, hits, MoreMatched: rows.Count > MaxHits);
    }

    // Cleaned and capped as a title typed in by hand is (SEC-5), then found anywhere in the text. Each backslash, % and
    // _ the clerk typed gets the escape character before it, so it's matched as itself (DATA-13). Backslashes go first,
    // or the escapes put before % and _ would be doubled too. Null when nothing's left, so the text matches every movie.
    private static string? LikePatternFor(string? text) =>
        WebAnswers.Clean(text, TmdbClient.MaxNameLength) is { } typed
            ? AnyText
                + typed
                    .Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
                    .Replace(AnyText, LikeEscape + AnyText, StringComparison.Ordinal)
                    .Replace(AnyCharacter, LikeEscape + AnyCharacter, StringComparison.Ordinal)
                + AnyText
            : null;

    // DATA-12: untracked, straight into rows, in one statement, so the hits and whether each is on the shelf come from
    // one moment. One row past MaxHits says whether more matched. NOCASE for the titles, as the other lists sort, then
    // the year, since remakes share titles, then the Id, so two movies with one title and year always read the same.
    private async Task<List<HitRow>> ReadRowsAsync(string? pattern, string? genre, int? year, string? rating, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await Filtered(db.Movies.AsNoTracking().Where(Listed), pattern, genre, year, rating)
            .OrderBy(m => EF.Functions.Collate(m.Title, NoCase))
            .ThenBy(m => m.Year)
            .ThenBy(m => m.Id)
            .Take(MaxHits + 1)
            .Select(m => new HitRow(m.Id, m.TmdbId, m.Title, m.Year, m.Rating, m.Cases.Any(c => c.Status == CaseStatus.OnShelf)))
            .ToListAsync(cancellationToken);
    }

    // Each filter the clerk asked for, and only those. SQL's = and LIKE never match a NULL, so a movie whose year,
    // rating or director is unknown never matches it, and one with no genres has none to match (DATA-3).
    private static IQueryable<Movie> Filtered(IQueryable<Movie> movies, string? pattern, string? genre, int? year, string? rating)
    {
        if (pattern is not null)
        {
            movies = movies.Where(m => EF.Functions.Like(m.Title, pattern, LikeEscape)
                || EF.Functions.Like(m.Director, pattern, LikeEscape)
                || m.Cast.Any(c => EF.Functions.Like(c.Name, pattern, LikeEscape)));
        }

        if (genre is not null)
        {
            movies = movies.Where(m => m.Genres.Any(g => g.Name == genre));
        }

        if (year is { } knownYear)
        {
            movies = movies.Where(m => m.Year == knownYear);
        }

        if (rating is not null)
        {
            movies = movies.Where(m => m.Rating == rating);
        }

        return movies;
    }

    // A movie entered by hand has no TMDB ID, and so no thumbnail. Posters answers null for one that's missing or can't
    // be read (GEN-7). One at a time: each is a few kilobytes on the register's own disk.
    private Task<byte[]?> ThumbAsync(int? tmdbId, CancellationToken cancellationToken) =>
        tmdbId is { } id ? posters.ReadAsync(id, PosterSize.Thumb, cancellationToken) : Task.FromResult<byte[]?>(null);

    // What the read gives each hit, with the TMDB ID that names its thumbnail.
    private sealed record HitRow(int MovieId, int? TmdbId, string Title, int? Year, string? Rating, bool OnShelf);
}
