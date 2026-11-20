using Microsoft.EntityFrameworkCore;
using Rewind.Core.Catalog;
using Rewind.Core.Data;
using Rewind.Core.Shelves;

namespace Rewind.Core.Tests.Shelves;

public sealed class ShelfMovieTests
{
    private const int NoSuchId = 999;

    // Build Plan 4.2's Done when, in the database: The Nightmare Before Christmas sits on both the Halloween and the
    // Christmas shelves.
    [Fact]
    public async Task SaveChanges_OneMovieOnTwoShelves_IsSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var halloween = await AddShelfAsync(db, "Halloween", ct);
        var christmas = await AddShelfAsync(db, "Christmas", ct);
        var nightmare = await AddMovieAsync(db, "The Nightmare Before Christmas", ct);
        db.ShelfMovies.AddRange(On(halloween, nightmare), On(christmas, nightmare));

        await db.SaveChangesAsync(ct);

        Assert.Equal([(halloween, nightmare), (christmas, nightmare)], await SavedAsync(db, ct));
    }

    [Fact]
    public async Task SaveChanges_TwoMoviesOnOneShelf_AreSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var halloween = await AddShelfAsync(db, "Halloween", ct);
        var nightmare = await AddMovieAsync(db, "The Nightmare Before Christmas", ct);
        var hocusPocus = await AddMovieAsync(db, "Hocus Pocus", ct);
        db.ShelfMovies.AddRange(On(halloween, nightmare), On(halloween, hocusPocus));

        await db.SaveChangesAsync(ct);

        Assert.Equal([(halloween, nightmare), (halloween, hocusPocus)], await SavedAsync(db, ct));
    }

    // DATA-11: a movie sits on a shelf once, which the table's key holds.
    [Fact]
    public async Task SaveChanges_SameMovieTwiceOnOneShelf_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var halloween = await AddShelfAsync(db, "Halloween", ct);
        var nightmare = await AddMovieAsync(db, "The Nightmare Before Christmas", ct);
        db.ShelfMovies.Add(On(halloween, nightmare));
        await db.SaveChangesAsync(ct);

        // A fresh context, since EF Core itself refuses to track a second row with a key the first context tracks.
        await using var fresh = rewind.OpenDb();
        fresh.ShelfMovies.Add(On(halloween, nightmare));

        await SqliteAssert.RefusesAsync(SqliteConstraint.PrimaryKey, () => fresh.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_ShelfThatIsntThere_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var nightmare = await AddMovieAsync(db, "The Nightmare Before Christmas", ct);

        db.ShelfMovies.Add(On(NoSuchId, nightmare));

        await SqliteAssert.RefusesAsync(SqliteConstraint.ForeignKey, () => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_MovieThatIsntThere_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var halloween = await AddShelfAsync(db, "Halloween", ct);

        db.ShelfMovies.Add(On(halloween, NoSuchId));

        await SqliteAssert.RefusesAsync(SqliteConstraint.ForeignKey, () => db.SaveChangesAsync(ct));
    }

    // DATA-4: a shelf keeps the movies on it, so a movie on a shelf can't be deleted. The movie is in no case, so only
    // its row on the shelf can refuse the delete.
    [Fact]
    public async Task SaveChanges_DeletingAMovieOnAShelf_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var halloween = await AddShelfAsync(db, "Halloween", ct);
        var nightmare = await AddMovieAsync(db, "The Nightmare Before Christmas", ct);
        db.ShelfMovies.Add(On(halloween, nightmare));
        await db.SaveChangesAsync(ct);

        // A fresh context tracks no row on a shelf, so the delete reaches SQLite, and the foreign key refuses it there.
        await using var fresh = rewind.OpenDb();
        fresh.Movies.Remove(await fresh.Movies.SingleAsync(ct));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Restrict, () => fresh.SaveChangesAsync(ct));
    }

    // DATA-4: a seasonal shelf is deactivated, never deleted. It has no member, so only the movie on it can refuse
    // the delete.
    [Fact]
    public async Task SaveChanges_DeletingAShelfWithAMovieOnIt_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var halloween = await AddShelfAsync(db, "Halloween", ct);
        var nightmare = await AddMovieAsync(db, "The Nightmare Before Christmas", ct);
        db.ShelfMovies.Add(On(halloween, nightmare));
        await db.SaveChangesAsync(ct);

        await using var fresh = rewind.OpenDb();
        fresh.Shelves.Remove(await fresh.Shelves.SingleAsync(ct));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Restrict, () => fresh.SaveChangesAsync(ct));
    }

    // The rows are what's on the shelves now, not their history (ADR-0015), so taking a movie off deletes its row,
    // and leaves the shelf and the movie.
    [Fact]
    public async Task SaveChanges_TakingAMovieOffAShelf_DeletesItsRow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var halloween = await AddShelfAsync(db, "Halloween", ct);
        var nightmare = await AddMovieAsync(db, "The Nightmare Before Christmas", ct);
        db.ShelfMovies.Add(On(halloween, nightmare));
        await db.SaveChangesAsync(ct);

        await using var fresh = rewind.OpenDb();
        fresh.ShelfMovies.Remove(await fresh.ShelfMovies.SingleAsync(ct));
        await fresh.SaveChangesAsync(ct);

        Assert.Empty(await SavedAsync(fresh, ct));
        Assert.Equal((1, 1), (await fresh.Shelves.CountAsync(ct), await fresh.Movies.CountAsync(ct)));
    }

    private static ShelfMovie On(int shelf, int movie) => new() { ShelfId = shelf, MovieId = movie };

    // A seasonal shelf, so it points at no member, and its delete can be refused only for a movie on it.
    private static async Task<int> AddShelfAsync(RewindDb db, string name, CancellationToken cancellationToken)
    {
        var shelf = new Shelf { Name = name, FromMonth = 10, FromDay = 1, ToMonth = 12, ToDay = 31 };
        db.Shelves.Add(shelf);
        await db.SaveChangesAsync(cancellationToken);
        return shelf.Id;
    }

    // A movie in no case, with no genres or cast, so only a shelf can point at it.
    private static async Task<int> AddMovieAsync(RewindDb db, string title, CancellationToken cancellationToken)
    {
        var movie = new Movie { Title = title };
        db.Movies.Add(movie);
        await db.SaveChangesAsync(cancellationToken);
        return movie.Id;
    }

    // What's on the shelves, by shelf, then movie, which for these tests is the order each was added in.
    private static async Task<List<(int Shelf, int Movie)>> SavedAsync(RewindDb db, CancellationToken cancellationToken) =>
        (await db.ShelfMovies
            .AsNoTracking()
            .OrderBy(sm => sm.ShelfId)
            .ThenBy(sm => sm.MovieId)
            .ToListAsync(cancellationToken))
            .Select(sm => (sm.ShelfId, sm.MovieId))
            .ToList();
}
