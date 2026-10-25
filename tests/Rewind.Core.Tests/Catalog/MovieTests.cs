using Microsoft.EntityFrameworkCore;
using Rewind.Core.Catalog;

namespace Rewind.Core.Tests.Catalog;

public sealed class MovieTests
{
    [Fact]
    public async Task SaveChanges_DeletingMovieWithCase_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using (var db = rewind.OpenDb())
        {
            db.Cases.Add(new Case { StoreLabel = "RV-00001", Movies = { new Movie { Title = "Halloween" } } });
            await db.SaveChangesAsync(ct);
        }

        // A fresh context tracks none of the case's rows, so the delete reaches SQLite, and the
        // foreign key refuses it there (DATA-4).
        await using var fresh = rewind.OpenDb();
        fresh.Movies.Remove(await fresh.Movies.SingleAsync(ct));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Restrict, () => fresh.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_DuplicateTmdbId_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        db.Movies.Add(new Movie { TmdbId = 948, Title = "Halloween" });
        await db.SaveChangesAsync(ct);

        db.Movies.Add(new Movie { TmdbId = 948, Title = "Halloween" });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Unique, () => db.SaveChangesAsync(ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SaveChanges_BlankTitle_IsRefused(string title)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        db.Movies.Add(new Movie { Title = title });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_TwoMoviesWithoutTmdbId_SavesBoth()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        db.Movies.Add(new Movie { Title = "Spring Recital" });
        await db.SaveChangesAsync(ct);

        // Movies entered by hand have no TMDB ID, so a second one mustn't collide with the first.
        db.Movies.Add(new Movie { Title = "Christmas Pageant" });
        await db.SaveChangesAsync(ct);

        Assert.Equal(2, await db.Movies.CountAsync(m => m.TmdbId == null, ct));
    }

    [Theory]
    [InlineData(0, null, null)]
    [InlineData(null, 0, null)]
    [InlineData(null, null, "")]
    [InlineData(null, null, " ")]
    [InlineData(null, null, "NR")]
    public async Task SaveChanges_PlaceholderForUnknown_IsRefused(int? year, int? runtimeMinutes, string? rating)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        // The year and runtime rows are refused by their own CHECKs, and the rating rows by
        // CK_Movies_Rating's IN check, since none of "", " " and NR is one of UsRatings.All.
        db.Movies.Add(new Movie { Title = "Halloween", Year = year, RuntimeMinutes = runtimeMinutes, Rating = rating });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    [Theory]
    [InlineData("TV-14")] // a TV rating
    [InlineData("PG13")] // PG-13, misspelled
    [InlineData("pg")]
    [InlineData(" PG ")]
    [InlineData("Unrated")] // a cover's word for no rating, which Rewind keeps as NULL
    public async Task SaveChanges_RatingOutsideTheFive_IsRefused(string rating)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        // pg and " PG " are refused too: the CHECK is exact, since readers such as the spinner's
        // rating cap compare against UsRatings.All as written.
        db.Movies.Add(new Movie { Title = "Halloween", Rating = rating });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_EachUsRating_Saves()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        db.Movies.AddRange(UsRatings.All.Select(rating => new Movie { Title = $"Rated {rating}", Rating = rating }));
        await db.SaveChangesAsync(ct);

        await using var fresh = rewind.OpenDb();
        var saved = await fresh.Movies.Select(m => m.Rating).ToListAsync(ct);
        Assert.Equal(UsRatings.All.Order(), saved.Order());
    }

    [Fact]
    public async Task SaveChanges_UnknownYearRuntimeAndRating_SavesNulls()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        db.Movies.Add(new Movie { Title = "Spring Recital" });
        await db.SaveChangesAsync(ct);

        await using var fresh = rewind.OpenDb();
        var movie = await fresh.Movies.AsNoTracking().SingleAsync(ct);
        Assert.Null(movie.Year);
        Assert.Null(movie.RuntimeMinutes);
        Assert.Null(movie.Rating);
    }

    [Fact]
    public async Task SaveChanges_TmdbFetchedAt_ReadsBackAsUtc()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var fetchedAt = rewind.Time.GetUtcNow().UtcDateTime;

        db.Movies.Add(new Movie { TmdbId = 948, Title = "Halloween", TmdbFetchedAt = fetchedAt });
        await db.SaveChangesAsync(ct);

        // SQLite hands DateTimes back as Unspecified, and RewindDb's conventions mark them UTC (DATA-8).
        await using var fresh = rewind.OpenDb();
        var readBack = await fresh.Movies.Select(m => m.TmdbFetchedAt).SingleAsync(ct);
        Assert.Equal(DateTimeKind.Utc, readBack?.Kind);
        Assert.Equal(fetchedAt.Ticks, readBack?.Ticks);
    }
}
