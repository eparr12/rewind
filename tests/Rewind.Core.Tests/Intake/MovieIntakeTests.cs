using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rewind.Core.Catalog;
using Rewind.Core.Intake;
using Rewind.Core.Metadata;

namespace Rewind.Core.Tests.Intake;

public sealed class MovieIntakeTests : IDisposable
{
    private const string Token = "eyJ.test-token.signature";

    // A different made-up JPEG for each size, so a test can tell which one went where.
    private static readonly byte[] PickerPoster = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x92];
    private static readonly byte[] RegisterPoster = [0xFF, 0xD8, 0xFF, 0xE0, 0x03, 0x42];
    private static readonly byte[] ThumbPoster = [0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x54];

    private readonly FakeTmdb _tmdb = new();
    private readonly HttpClient _http;

    public MovieIntakeTests()
    {
        _http = new HttpClient(_tmdb);
        _tmdb.AnswerJson(FakeTmdb.SearchFor("Halloween"), TmdbJson.HalloweenSearch);
        _tmdb.AnswerJson(FakeTmdb.MovieFor(TmdbJson.HalloweenId), TmdbJson.Movie());
        _tmdb.AnswerJpeg(FakeTmdb.PosterFor("w92", TmdbJson.HalloweenPoster), PickerPoster);
        _tmdb.AnswerJpeg(FakeTmdb.PosterFor("w342", TmdbJson.HalloweenPoster), RegisterPoster);
        _tmdb.AnswerJpeg(FakeTmdb.PosterFor("w154", TmdbJson.HalloweenPoster), ThumbPoster);
    }

    public void Dispose() => _http.Dispose();

    // Build Plan 2.3's check: picking Halloween (1978) saves the right poster, director, cast and
    // rating. This test and the next one cover it.
    [Fact]
    public async Task AddAsync_Halloween_SavesItsRatingDirectorAndCast()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        await Intake(rewind).AddAsync(TmdbJson.HalloweenId, ct);

        await using var db = rewind.OpenDb();
        var movie = await db.Movies.AsNoTracking().Include(m => m.Cast).SingleAsync(ct);
        Assert.Equal("R", movie.Rating);
        Assert.Equal("John Carpenter", movie.Director);
        string[] topFive = ["Donald Pleasence", "Jamie Lee Curtis", "Nancy Kyes", "P. J. Soles", "Charles Cyphers"];
        Assert.Equal(topFive, movie.Cast.OrderBy(c => c.BillingOrder).Select(c => c.Name));
        Assert.Equal([0, 1, 2, 3, 4], movie.Cast.Select(c => c.BillingOrder).Order());
    }

    [Fact]
    public async Task AddAsync_Halloween_SavesItsPosterAtBothSizes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        await Intake(rewind).AddAsync(TmdbJson.HalloweenId, ct);

        await using var db = rewind.OpenDb();
        Assert.Equal("948.jpg", await db.Movies.Select(m => m.PosterFile).SingleAsync(ct));
        Assert.Equal(RegisterPoster, await File.ReadAllBytesAsync(Path.Combine(rewind.Paths.Posters, "948.jpg"), ct));
        Assert.Equal(ThumbPoster, await File.ReadAllBytesAsync(Path.Combine(rewind.Paths.Thumbs, "948.jpg"), ct));
    }

    [Fact]
    public async Task AddAsync_Halloween_SavesItsYearRuntimeGenresAndFetchTime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        await Intake(rewind).AddAsync(TmdbJson.HalloweenId, ct);

        await using var db = rewind.OpenDb();
        var movie = await db.Movies.AsNoTracking().Include(m => m.Genres).SingleAsync(ct);
        Assert.Equal(1978, movie.Year);
        Assert.Equal(91, movie.RuntimeMinutes);
        Assert.Equal(["Horror", "Thriller"], movie.Genres.Select(g => g.Name).Order());
        Assert.Equal(rewind.Time.GetUtcNow().UtcDateTime, movie.TmdbFetchedAt); // NET-8: from the injected clock
    }

    [Fact]
    public async Task AddAsync_Halloween_ShowsWhatWasSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var add = await Intake(rewind).AddAsync(TmdbJson.HalloweenId, ct);

        Assert.Equal(AddStatus.Added, add.Status);
        var card = Assert.IsType<MovieCard>(add.Movie);
        Assert.Equal(("Halloween", "R", "John Carpenter"), (card.Title, card.Rating, card.Director));
        Assert.Equal(5, card.Cast.Count);
        Assert.Equal(RegisterPoster, card.Poster);
    }

    // A second edition joins the movie we already have (Overview: intake), so nothing is fetched.
    [Fact]
    public async Task AddAsync_MovieAlreadyOwned_LeavesItAsItIs()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using (var db = rewind.OpenDb())
        {
            db.Movies.Add(new Movie { TmdbId = TmdbJson.HalloweenId, Title = "Halloween", Year = 1978 });
            await db.SaveChangesAsync(ct);
        }

        var add = await Intake(rewind).AddAsync(TmdbJson.HalloweenId, ct);

        Assert.Equal(AddStatus.AlreadyOwned, add.Status);
        Assert.Equal("Halloween", add.Movie?.Title);
        Assert.Empty(_tmdb.Requests);
        await using var fresh = rewind.OpenDb();
        Assert.Equal(1, await fresh.Movies.CountAsync(ct));
    }

    // Genres.TmdbId is unique, so a genre the database has is linked, not added again.
    [Fact]
    public async Task AddAsync_GenreAlreadyKnown_LinksIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        int horrorId;
        await using (var db = rewind.OpenDb())
        {
            var horror = new Genre { TmdbId = 27, Name = "Horror" };
            db.Genres.Add(horror);
            await db.SaveChangesAsync(ct);
            horrorId = horror.Id;
        }

        await Intake(rewind).AddAsync(TmdbJson.HalloweenId, ct);

        await using var fresh = rewind.OpenDb();
        Assert.Equal(2, await fresh.Genres.CountAsync(ct));
        Assert.Contains(horrorId, await fresh.MovieGenres.Select(mg => mg.GenreId).ToListAsync(ct));
    }

    // DATA-3 end to end: TMDB's 0 and "" become NULL, which the database's checks would refuse otherwise.
    [Fact]
    public async Task AddAsync_UnknownRuntimeAndRating_SavesNulls()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        _tmdb.AnswerJson(
            FakeTmdb.MovieFor(TmdbJson.HalloweenStoreId),
            TmdbJson.Movie(title: "Halloween Store", releaseDate: "", runtime: 0, overview: "", posterPath: null, releases: [("US", "")]));

        var add = await Intake(rewind).AddAsync(TmdbJson.HalloweenStoreId, ct);

        Assert.Equal(AddStatus.Added, add.Status);
        await using var db = rewind.OpenDb();
        var movie = await db.Movies.AsNoTracking().SingleAsync(ct);
        Assert.Null(movie.Year);
        Assert.Null(movie.RuntimeMinutes);
        Assert.Null(movie.Rating);
        Assert.Null(movie.Overview);
        Assert.Null(movie.PosterFile);
    }

    // GEN-7: a missing poster doesn't stop the movie. PosterFile still names it, so it can be
    // downloaded later, as after a restore.
    [Fact]
    public async Task AddAsync_PosterWontDownload_AddsTheMovieWithoutIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        _tmdb.Throw(FakeTmdb.PosterFor("w342", TmdbJson.HalloweenPoster), new HttpRequestException("No such host is known."));
        _tmdb.Throw(FakeTmdb.PosterFor("w154", TmdbJson.HalloweenPoster), new HttpRequestException("No such host is known."));

        var add = await Intake(rewind).AddAsync(TmdbJson.HalloweenId, ct);

        Assert.Equal(AddStatus.Added, add.Status);
        Assert.Null(add.Movie?.Poster);
        await using var db = rewind.OpenDb();
        Assert.Equal("948.jpg", await db.Movies.Select(m => m.PosterFile).SingleAsync(ct));
        Assert.False(Directory.Exists(rewind.Paths.Posters));
    }

    // GEN-7: a poster that can't be saved, such as when the data folder's permissions are wrong,
    // leaves the movie added without it, and the other size is still saved.
    [Fact]
    public async Task AddAsync_PosterWontSave_AddsTheMovieWithoutIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        // A file where the posters folder should be: creating the folder fails, on Windows and on Linux.
        await File.WriteAllTextAsync(rewind.Paths.Posters, "", ct);

        var add = await Intake(rewind).AddAsync(TmdbJson.HalloweenId, ct);

        Assert.Equal(AddStatus.Added, add.Status);
        Assert.Null(add.Movie?.Poster);
        Assert.Equal(ThumbPoster, await File.ReadAllBytesAsync(Path.Combine(rewind.Paths.Thumbs, "948.jpg"), ct));
        await using var db = rewind.OpenDb();
        Assert.Equal("948.jpg", await db.Movies.Select(m => m.PosterFile).SingleAsync(ct));
    }

    // The movie is saved before its posters, so a movie that fails to save leaves no posters that
    // nothing would ever clean up.
    [Fact]
    public async Task AddAsync_MovieWontSave_SavesNoPosters()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using (var db = rewind.OpenDb())
        {
            // Stands in for any save that fails, such as one into a locked file.
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER RefuseMovies BEFORE INSERT ON Movies BEGIN SELECT RAISE(ABORT, 'refused'); END;", ct);
        }

        await Assert.ThrowsAsync<DbUpdateException>(() => Intake(rewind).AddAsync(TmdbJson.HalloweenId, ct));

        Assert.False(Directory.Exists(rewind.Paths.Posters));
        Assert.False(Directory.Exists(rewind.Paths.Thumbs));
    }

    // NET-12: TMDB not answering is an outcome, and nothing is saved.
    [Fact]
    public async Task AddAsync_TmdbDoesNotAnswer_SavesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        _tmdb.Throw(FakeTmdb.MovieFor(TmdbJson.HalloweenId), new HttpRequestException("No such host is known."));

        var add = await Intake(rewind).AddAsync(TmdbJson.HalloweenId, ct);

        Assert.Equal(new MovieAdd(AddStatus.TryLater, null), add);
        await using var db = rewind.OpenDb();
        Assert.Equal(0, await db.Movies.CountAsync(ct));
        Assert.False(Directory.Exists(rewind.Paths.Posters));
    }

    [Fact]
    public async Task AddAsync_NoToken_IsNotSetUp()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var add = await Intake(rewind, token: "").AddAsync(TmdbJson.HalloweenId, ct);

        Assert.Equal(AddStatus.NotSetUp, add.Status);
    }

    // Matches without a poster, or whose poster won't download, still show.
    [Fact]
    public async Task SearchAsync_Halloween_BringsEachMatchItsPoster()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var search = await Intake(rewind).SearchAsync("Halloween", ct);

        Assert.Equal(TmdbStatus.Ok, search.Status);
        Assert.Equal(PickerPoster, search.Matches.Single(m => m.TmdbId == TmdbJson.HalloweenId).Poster);
        Assert.Null(search.Matches.Single(m => m.TmdbId == TmdbJson.Halloween2018Id).Poster); // no w92 answer set up
        Assert.Null(search.Matches.Single(m => m.TmdbId == TmdbJson.HalloweenStoreId).Poster); // TMDB has none
    }

    private MovieIntake Intake(TempRewind rewind, string token = Token) =>
        new(
            new TmdbClient(_http, Options.Create(new TmdbOptions { AccessToken = token }), NullLogger<TmdbClient>.Instance),
            new Posters(Options.Create(rewind.Paths), NullLogger<Posters>.Instance),
            rewind.DbFactory,
            rewind.Time,
            NullLogger<MovieIntake>.Instance);
}
