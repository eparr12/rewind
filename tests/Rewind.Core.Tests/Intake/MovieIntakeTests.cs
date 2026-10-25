using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Rewind.Core.Catalog;
using Rewind.Core.Data;
using Rewind.Core.Intake;
using Rewind.Core.Metadata;

namespace Rewind.Core.Tests.Intake;

public sealed class MovieIntakeTests : IDisposable
{
    private const string Token = "eyJ.test-token.signature";

    // The case being entered: the next store label, and the barcode its DVD came with.
    private const string Label = "RV-00042";
    private const string FactoryUpc = "036000291452";

    // The barcode of the case Halloween is on already, in AddHalloweenAsync: another edition's.
    private const string FirstEditionUpc = "025192105418";

    // A different made-up JPEG for each size, so a test can tell which one went where.
    private static readonly byte[] PickerPoster = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x92];
    private static readonly byte[] RegisterPoster = [0xFF, 0xD8, 0xFF, 0xE0, 0x03, 0x42];
    private static readonly byte[] ThumbPoster = [0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x54];

    private static readonly string[] TopFive = ["Donald Pleasence", "Jamie Lee Curtis", "Nancy Kyes", "P. J. Soles", "Charles Cyphers"];

    private readonly FakeTmdb _tmdb = new();
    private readonly HttpClient _http;

    // EnterByHand's clock: 11 PM on New Year's Eve 2026 on a register clock four hours behind UTC,
    // where it's 3 AM on January 1, 2027 already. Next year, the latest a movie can be from, is
    // 2027 on the register's calendar but 2028 on UTC's, so the year tests tell the two apart.
    private readonly FakeTimeProvider _registerClock = new(new DateTimeOffset(2027, 1, 1, 3, 0, 0, TimeSpan.Zero));

    public MovieIntakeTests()
    {
        _registerClock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Register", TimeSpan.FromHours(-4), "Register", "Register"));
        _http = new HttpClient(_tmdb);
        _tmdb.AnswerJson(FakeTmdb.SearchFor("Halloween"), TmdbJson.HalloweenSearch);
        _tmdb.AnswerJson(FakeTmdb.MovieFor(TmdbJson.HalloweenId), TmdbJson.Movie());
        _tmdb.AnswerJpeg(FakeTmdb.PosterFor("w92", TmdbJson.HalloweenPoster), PickerPoster);
        _tmdb.AnswerJpeg(FakeTmdb.PosterFor("w342", TmdbJson.HalloweenPoster), RegisterPoster);
        _tmdb.AnswerJpeg(FakeTmdb.PosterFor("w154", TmdbJson.HalloweenPoster), ThumbPoster);
    }

    public void Dispose() => _http.Dispose();

    // Build Plan 2.3's check, a step later (ADR-0005): picking Halloween (1978) fetches the right
    // poster, director, cast and rating, and the label scan saves them. This test and the next one
    // cover the pick; the ShelveAsync_Halloween tests cover the save.
    [Fact]
    public async Task PickAsync_Halloween_FetchesItsRatingDirectorAndCast()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var pick = await Intake(rewind).PickAsync(TmdbJson.HalloweenId, ct);

        Assert.Equal(PickStatus.Fetched, pick.Status);
        var halloween = Assert.IsType<CaseMovieEntry.Fetched>(pick.Entry);
        Assert.Equal(("Halloween", 1978, "R", "John Carpenter"), (halloween.Title, halloween.Year, halloween.Rating, halloween.Director));
        Assert.Equal(TopFive, halloween.Cast);
    }

    [Fact]
    public async Task PickAsync_Halloween_HoldsItsPosterAtBothSizes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var pick = await Intake(rewind).PickAsync(TmdbJson.HalloweenId, ct);

        var halloween = Assert.IsType<CaseMovieEntry.Fetched>(pick.Entry);
        Assert.Equal(RegisterPoster, halloween.PostersBySize[PosterSize.Register]);
        Assert.Equal(ThumbPoster, halloween.PostersBySize[PosterSize.Thumb]);
        Assert.Equal(RegisterPoster, halloween.Poster); // the one the screen shows
    }

    // ADR-0005: a wrong pick costs nothing, since nothing is saved until the label scan.
    [Fact]
    public async Task PickAsync_NewMovie_WritesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        await Intake(rewind).PickAsync(TmdbJson.HalloweenId, ct);

        await using var db = rewind.OpenDb();
        Assert.Equal(0, await db.Movies.CountAsync(ct));
        Assert.Equal(0, await db.Genres.CountAsync(ct));
        Assert.False(Directory.Exists(rewind.Paths.Posters));
        Assert.False(Directory.Exists(rewind.Paths.Thumbs));
    }

    // A second edition joins the movie we already have (Overview: intake), so TMDB isn't asked.
    [Fact]
    public async Task PickAsync_OwnedMovie_FetchesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloweenId = await AddHalloweenAsync(rewind, ct);

        var pick = await Intake(rewind).PickAsync(TmdbJson.HalloweenId, ct);

        Assert.Equal(PickStatus.Owned, pick.Status);
        Assert.Equal(halloweenId, Assert.IsType<CaseMovieEntry.Owned>(pick.Entry).MovieId);
        Assert.Empty(_tmdb.Requests);
    }

    // The screen lists a movie the collection has as it was saved: its facts, its cast in billing
    // order and its genres by name. AddHalloweenAsync saves the cast bottom of the bill first and the
    // genres the other way round, so neither comes out in order unless the read puts it in order.
    [Fact]
    public async Task PickAsync_OwnedMovie_ListsItAsSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloweenId = await AddHalloweenAsync(rewind, ct);

        var pick = await Intake(rewind).PickAsync(TmdbJson.HalloweenId, ct);

        var halloween = Assert.IsType<CaseMovieEntry.Owned>(pick.Entry);
        Assert.Equal(
            (halloweenId, TmdbJson.HalloweenId, "Halloween", 1978, 91, "R", "John Carpenter"),
            (halloween.MovieId, halloween.TmdbId, halloween.Title, halloween.Year, halloween.RuntimeMinutes, halloween.Rating, halloween.Director));
        Assert.Equal(TopFive, halloween.Cast);
        Assert.Equal(["Horror", "Thriller"], halloween.Genres);
        Assert.Null(halloween.Poster); // none was saved for it here
    }

    // The screen lists a movie the collection has with the poster saved for it.
    [Fact]
    public async Task PickAsync_OwnedMovie_ShowsItsSavedPoster()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddHalloweenAsync(rewind, ct);
        Directory.CreateDirectory(rewind.Paths.Posters);
        await File.WriteAllBytesAsync(Path.Combine(rewind.Paths.Posters, "948.jpg"), RegisterPoster, ct);

        var pick = await Intake(rewind).PickAsync(TmdbJson.HalloweenId, ct);

        Assert.Equal(RegisterPoster, pick.Entry?.Poster);
    }

    // GEN-7: a poster that won't download doesn't stop the pick.
    [Fact]
    public async Task PickAsync_PosterWontDownload_HoldsTheMovieWithoutIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        _tmdb.Throw(FakeTmdb.PosterFor("w342", TmdbJson.HalloweenPoster), new HttpRequestException("No such host is known."));
        _tmdb.Throw(FakeTmdb.PosterFor("w154", TmdbJson.HalloweenPoster), new HttpRequestException("No such host is known."));

        var pick = await Intake(rewind).PickAsync(TmdbJson.HalloweenId, ct);

        var halloween = Assert.IsType<CaseMovieEntry.Fetched>(pick.Entry);
        Assert.Empty(halloween.PostersBySize);
        Assert.Null(halloween.Poster);
    }

    // NET-12: TMDB not answering is an outcome.
    [Fact]
    public async Task PickAsync_TmdbDoesNotAnswer_IsTryLater()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        _tmdb.Throw(FakeTmdb.MovieFor(TmdbJson.HalloweenId), new HttpRequestException("No such host is known."));

        var pick = await Intake(rewind).PickAsync(TmdbJson.HalloweenId, ct);

        Assert.Equal(new MoviePick(PickStatus.TryLater, null), pick);
    }

    [Fact]
    public async Task PickAsync_NoToken_IsNotSetUp()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var pick = await Intake(rewind, token: "").PickAsync(TmdbJson.HalloweenId, ct);

        Assert.Equal(new MoviePick(PickStatus.NotSetUp, null), pick);
    }

    // TMDB answers 404 for a movie it doesn't have, as the fake does for any it wasn't given.
    [Fact]
    public async Task PickAsync_NoSuchMovie_IsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var pick = await Intake(rewind).PickAsync(TmdbJson.Halloween2018Id, ct);

        Assert.Equal(new MoviePick(PickStatus.NotFound, null), pick);
    }

    // A movie typed in by hand is only checked: nothing is saved, and TMDB isn't asked. HandEntryIntake's
    // database throws if it's opened at all.
    [Fact]
    public void EnterByHand_Movie_AsksTmdbNothing()
    {
        var entry = HandEntryIntake().EnterByHand("The Haddonfield Story", "1985", "92", "PG");

        Assert.Equal(HandEntryStatus.Entered, entry.Status);
        Assert.Empty(_tmdb.Requests);
    }

    // Cleaned as TMDB's titles are: trimmed, and a line break becomes a space, so the title stays on
    // one line on the screen and in the log, where a typed line break could forge a line (SEC-5).
    [Theory]
    [InlineData("  The Haddonfield Story  ")]
    [InlineData("The Haddonfield\nStory")]
    public void EnterByHand_UntidyTitle_IsCleaned(string title)
    {
        var entry = HandEntryIntake().EnterByHand(title, "", "", "");

        Assert.Equal("The Haddonfield Story", entry.Entry?.Title);
    }

    // SEC-5: capped as TMDB's titles are.
    [Fact]
    public void EnterByHand_LongTitle_CapsIt()
    {
        var entry = HandEntryIntake().EnterByHand(new string('A', TmdbClient.MaxNameLength + 50), "", "", "");

        Assert.Equal(TmdbClient.MaxNameLength, entry.Entry?.Title.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EnterByHand_BlankTitle_IsRefused(string title)
    {
        var entry = HandEntryIntake().EnterByHand(title, "1985", "92", "PG");

        Assert.Equal(new HandEntry(HandEntryStatus.NoTitle, null), entry);
    }

    // DATA-3: what the clerk leaves blank is unknown, so null, never 0 or blank text.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnterByHand_BlankYearRuntimeAndRating_AreNull(string? blank)
    {
        var entry = HandEntryIntake().EnterByHand("The Haddonfield Story", blank, blank, blank);

        var movie = Assert.IsType<CaseMovieEntry.ByHand>(entry.Entry);
        Assert.Equal((null, null, null), (movie.Year, movie.RuntimeMinutes, movie.Rating));
    }

    // Spaces around a number don't matter, and the rating is kept in its usual form.
    [Fact]
    public void EnterByHand_YearRuntimeAndRating_AreKept()
    {
        var entry = HandEntryIntake().EnterByHand("The Haddonfield Story", " 1985 ", "92", "pg-13");

        var movie = Assert.IsType<CaseMovieEntry.ByHand>(entry.Entry);
        Assert.Equal((1985, 92, "PG-13"), (movie.Year, movie.RuntimeMinutes, movie.Rating));
    }

    // A slip is caught: a year before the oldest film, after next year on the register's calendar
    // (2028 would still be next year by UTC), or that isn't a year in digits. NumberStyles.None also
    // turns away another script's digits (SEC-5).
    [Theory]
    [InlineData("1887")]
    [InlineData("2028")]
    [InlineData("85")]
    [InlineData("198S")]
    [InlineData("-1985")]
    [InlineData("1,985")]
    [InlineData("1985.0")]
    [InlineData("\u0661\u0669\u0668\u0665")] // 1985 in Arabic-Indic digits
    public void EnterByHand_BadYear_IsRefused(string year)
    {
        var entry = HandEntryIntake().EnterByHand("The Haddonfield Story", year, "", "");

        Assert.Equal(new HandEntry(HandEntryStatus.NotAYear, null), entry);
    }

    // The oldest film's year, and next year on the register's calendar, though it's 2027 already by UTC.
    [Theory]
    [InlineData("1888", 1888)]
    [InlineData("2027", 2027)]
    public void EnterByHand_YearAtItsBounds_IsKept(string year, int kept)
    {
        var entry = HandEntryIntake().EnterByHand("The Haddonfield Story", year, "", "");

        Assert.Equal(kept, entry.Entry?.Year);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("601")]
    [InlineData("1h30")]
    [InlineData("+92")]
    public void EnterByHand_BadRuntime_IsRefused(string runtimeMinutes)
    {
        var entry = HandEntryIntake().EnterByHand("The Haddonfield Story", "", runtimeMinutes, "");

        Assert.Equal(new HandEntry(HandEntryStatus.NotARuntime, null), entry);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("600", 600)]
    public void EnterByHand_RuntimeAtItsBounds_IsKept(string runtimeMinutes, int kept)
    {
        var entry = HandEntryIntake().EnterByHand("The Haddonfield Story", "", runtimeMinutes, "");

        Assert.Equal(kept, entry.Entry?.RuntimeMinutes);
    }

    // DATA-3: NR is TMDB's "No rating information", which the clerk leaves blank instead.
    [Theory]
    [InlineData("NR")]
    [InlineData("PG13")]
    [InlineData("TV-14")]
    public void EnterByHand_BadRating_IsRefused(string rating)
    {
        var entry = HandEntryIntake().EnterByHand("The Haddonfield Story", "", "", rating);

        Assert.Equal(new HandEntry(HandEntryStatus.NotARating, null), entry);
    }

    // Build Plan 2.3's check, saved at the label scan (ADR-0005).
    [Fact]
    public async Task ShelveAsync_Halloween_SavesItsRatingDirectorAndCast()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var intake = Intake(rewind);
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);

        await intake.ShelveAsync(Label, FactoryUpc, [halloween], ct);

        await using var db = rewind.OpenDb();
        var movie = await db.Movies.AsNoTracking().Include(m => m.Cast).SingleAsync(ct);
        Assert.Equal(("R", "John Carpenter"), (movie.Rating, movie.Director));
        Assert.Equal(TopFive, movie.Cast.OrderBy(c => c.BillingOrder).Select(c => c.Name));
        Assert.Equal([0, 1, 2, 3, 4], movie.Cast.Select(c => c.BillingOrder).Order());
    }

    [Fact]
    public async Task ShelveAsync_Halloween_SavesItsPosterAtBothSizes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var intake = Intake(rewind);
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);

        await intake.ShelveAsync(Label, FactoryUpc, [halloween], ct);

        await using var db = rewind.OpenDb();
        Assert.Equal("948.jpg", await db.Movies.Select(m => m.PosterFile).SingleAsync(ct));
        Assert.Equal(RegisterPoster, await File.ReadAllBytesAsync(Path.Combine(rewind.Paths.Posters, "948.jpg"), ct));
        Assert.Equal(ThumbPoster, await File.ReadAllBytesAsync(Path.Combine(rewind.Paths.Thumbs, "948.jpg"), ct));
    }

    // NET-8: TmdbFetchedAt is when TMDB answered the pick, not when the label was scanned, since
    // TMDB's terms count from the fetch.
    [Fact]
    public async Task ShelveAsync_Halloween_SavesItsYearRuntimeGenresAndFetchTime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var intake = Intake(rewind);
        var pickedAt = rewind.Time.GetUtcNow().UtcDateTime;
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);
        rewind.Time.Advance(TimeSpan.FromMinutes(2)); // peeling the label and sticking it on

        await intake.ShelveAsync(Label, FactoryUpc, [halloween], ct);

        await using var db = rewind.OpenDb();
        var movie = await db.Movies.AsNoTracking().Include(m => m.Genres).SingleAsync(ct);
        Assert.Equal((1978, 91), (movie.Year, movie.RuntimeMinutes));
        Assert.Equal(["Horror", "Thriller"], movie.Genres.Select(g => g.Name).Order());
        Assert.Equal(pickedAt, movie.TmdbFetchedAt);
    }

    [Fact]
    public async Task ShelveAsync_NewMovie_SavesTheCaseOnTheShelfWithItsBarcode()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var intake = Intake(rewind);
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);

        await intake.ShelveAsync(Label, FactoryUpc, [halloween], ct);

        await using var db = rewind.OpenDb();
        var shelved = await db.Cases.AsNoTracking().Include(c => c.Movies).SingleAsync(ct);
        Assert.Equal((Label, FactoryUpc, CaseStatus.OnShelf), (shelved.StoreLabel, shelved.FactoryUpc, shelved.Status));
        Assert.Equal(["Halloween"], shelved.Movies.Select(m => m.Title));
    }

    // Build Plan 2.4's check, first half: a double-feature disc becomes one case holding two movies.
    [Fact]
    public async Task ShelveAsync_TwoNewMovies_SavesOneCaseHoldingBoth()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        AnswerHalloween2018();
        var intake = Intake(rewind);
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);
        var sequel = await PickedAsync(intake, TmdbJson.Halloween2018Id);

        await intake.ShelveAsync(Label, FactoryUpc, [halloween, sequel], ct);

        await using var db = rewind.OpenDb();
        var shelved = await db.Cases.AsNoTracking().Include(c => c.Movies).SingleAsync(ct);
        Assert.Equal([1978, 2018], shelved.Movies.Select(m => m.Year).Order());
        Assert.Equal(2, await db.Movies.CountAsync(ct));
    }

    // Build Plan 2.4's check, second half: a second edition of a movie joins the movie we already
    // have, so the collection still has it once, now in two cases.
    [Fact]
    public async Task ShelveAsync_OwnedMovie_JoinsIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloweenId = await AddHalloweenAsync(rewind, ct);
        var intake = Intake(rewind);
        var secondEdition = await PickedAsync(intake, TmdbJson.HalloweenId);

        await intake.ShelveAsync(Label, FactoryUpc, [secondEdition], ct);

        await using var db = rewind.OpenDb();
        Assert.Equal(halloweenId, await db.Movies.Select(m => m.Id).SingleAsync(ct));
        var shelvedId = await db.Cases.Where(c => c.StoreLabel == Label).Select(c => c.Id).SingleAsync(ct);
        Assert.Equal([halloweenId], await db.CaseMovies.Where(cm => cm.CaseId == shelvedId).Select(cm => cm.MovieId).ToListAsync(ct));
    }

    // A box set of every kind: the collection has disc 1's movie already, disc 2's is new to it, and
    // disc 3's isn't on TMDB. One save takes all three, through each of the ways a case gets a movie.
    [Fact]
    public async Task ShelveAsync_BoxSetOfEachKind_SavesOneCaseHoldingAllThree()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloweenId = await AddHalloweenAsync(rewind, ct);
        AnswerHalloween2018();
        var intake = Intake(rewind);
        var owned = await PickedAsync(intake, TmdbJson.HalloweenId);
        var fetched = await PickedAsync(intake, TmdbJson.Halloween2018Id);
        var typed = Assert.IsType<CaseMovieEntry.ByHand>(intake.EnterByHand("The Haddonfield Story", "1985", "", "").Entry);

        var shelving = await intake.ShelveAsync(Label, FactoryUpc, [owned, fetched, typed], ct);

        Assert.Equal(ShelveStatus.Shelved, shelving.Status);
        await using var db = rewind.OpenDb();
        var shelvedId = await db.Cases.Where(c => c.StoreLabel == Label).Select(c => c.Id).SingleAsync(ct);
        var inTheCase = await db.CaseMovies.Where(cm => cm.CaseId == shelvedId).Select(cm => cm.MovieId).ToListAsync(ct);
        Assert.Equal(3, inTheCase.Count);
        Assert.Contains(halloweenId, inTheCase);
        Assert.Equal(2, await db.Movies.CountAsync(m => m.Id != halloweenId, ct)); // the two new ones
    }

    // NET-12: a label another case has is an outcome, and nothing is saved, not even the new movie.
    [Fact]
    public async Task ShelveAsync_LabelInUse_SavesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await TakeTheLabelAsync(rewind, ct);
        var intake = Intake(rewind);
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);

        var shelving = await intake.ShelveAsync(Label, FactoryUpc, [halloween], ct);

        Assert.Equal(new Shelving(ShelveStatus.LabelInUse, null), shelving);
        await using var db = rewind.OpenDb();
        Assert.Equal(0, await db.Movies.CountAsync(ct));
        Assert.Equal(1, await db.Cases.CountAsync(ct));
        Assert.False(Directory.Exists(rewind.Paths.Posters));
    }

    // NET-12, OBS-3: a label in use is a slip of the clerk's, found before the save, so the log has
    // no error for it. Left to the unique index, EF Core would log the refused save as one.
    [Fact]
    public async Task ShelveAsync_LabelInUse_LogsNoError()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await TakeTheLabelAsync(rewind, ct);
        var log = new ListLoggerProvider();
        using var logged = new LoggedContexts(rewind, log);
        var intake = Intake(rewind, dbFactory: logged);
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);

        await intake.ShelveAsync(Label, FactoryUpc, [halloween], ct);

        Assert.Contains(log.Entries, entry => entry.Category.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)); // the log is EF Core's
        Assert.DoesNotContain(log.Entries, entry => entry.Level >= LogLevel.Error);
    }

    // OBS-3: a label turning up on two cases is worth finding in the log a month on, such as when a
    // sheet of labels was printed twice (Build Plan 2.5).
    [Fact]
    public async Task ShelveAsync_LabelInUse_LogsTheLabel()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await TakeTheLabelAsync(rewind, ct);
        var log = new ListLogger<MovieIntake>();
        var intake = Intake(rewind, logger: log);
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);

        await intake.ShelveAsync(Label, FactoryUpc, [halloween], ct);

        Assert.Contains(
            (LogLevel.Information, "Store label RV-00042 is on another case already, so nothing was saved", (Exception?)null),
            log.Entries);
    }

    // DATA-11: the label is free when ShelveAsync looks, then another case takes it before the save,
    // and the unique index refuses it. A trigger can't stand in for that other save: SQLite rolls a
    // trigger's insert back along with the insert that fails, so the label would look free again
    // when ShelveAsync looks after the refusal. The interceptor saves the other case for real,
    // through a context of its own, just before this save.
    [Fact]
    public async Task ShelveAsync_LabelTakenDuringTheSave_IsLabelInUse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var intake = Intake(rewind, dbFactory: new LabelTakenJustBeforeEachSave(rewind));
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);

        var shelving = await intake.ShelveAsync(Label, FactoryUpc, [halloween], ct);

        Assert.Equal(new Shelving(ShelveStatus.LabelInUse, null), shelving);
        await using var db = rewind.OpenDb();
        Assert.Equal(0, await db.Movies.CountAsync(ct));
        Assert.False(Directory.Exists(rewind.Paths.Posters));
    }

    // DATA-7, the proof of one save: a case the database refuses takes its new movie with it, and
    // no poster is saved for a movie that isn't.
    [Fact]
    public async Task ShelveAsync_CaseRefused_SavesNoMovie()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using (var db = rewind.OpenDb())
        {
            // Stands in for any save that fails, such as one into a locked file.
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER RefuseCases BEFORE INSERT ON Cases BEGIN SELECT RAISE(ABORT, 'refused'); END;", ct);
        }

        var intake = Intake(rewind);
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);

        await Assert.ThrowsAsync<DbUpdateException>(() => intake.ShelveAsync(Label, FactoryUpc, [halloween], ct));

        await using var fresh = rewind.OpenDb();
        Assert.Equal(0, await fresh.Movies.CountAsync(ct));
        Assert.False(Directory.Exists(rewind.Paths.Posters));
        Assert.False(Directory.Exists(rewind.Paths.Thumbs));
    }

    // Genres.TmdbId is unique, so two new movies saved together that share a new genre share its
    // row. Made once for each movie, the second would make the whole save fail.
    [Fact]
    public async Task ShelveAsync_TwoNewMoviesShareANewGenre_SavesOneGenre()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        AnswerHalloween2018(); // Horror and Thriller, as the 1978 one is
        var intake = Intake(rewind);
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);
        var sequel = await PickedAsync(intake, TmdbJson.Halloween2018Id);

        await intake.ShelveAsync(Label, FactoryUpc, [halloween, sequel], ct);

        await using var db = rewind.OpenDb();
        Assert.Equal(["Horror", "Thriller"], (await db.Genres.Select(g => g.Name).ToListAsync(ct)).Order());
        Assert.Equal(4, await db.MovieGenres.CountAsync(ct));
    }

    // Genres.TmdbId is unique, so a genre the database has is linked, not added again.
    [Fact]
    public async Task ShelveAsync_GenreAlreadyKnown_LinksIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        int horrorId;
        await using (var db = rewind.OpenDb())
        {
            var horror = new Genre { TmdbId = TmdbJson.HorrorId, Name = "Horror" };
            db.Genres.Add(horror);
            await db.SaveChangesAsync(ct);
            horrorId = horror.Id;
        }

        var intake = Intake(rewind);
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);

        await intake.ShelveAsync(Label, FactoryUpc, [halloween], ct);

        await using var fresh = rewind.OpenDb();
        Assert.Equal(2, await fresh.Genres.CountAsync(ct));
        Assert.Contains(horrorId, await fresh.MovieGenres.Select(mg => mg.GenreId).ToListAsync(ct));
    }

    // The screen doesn't list a movie twice, and this is the guard behind it: Movies.TmdbId and the
    // case's links each take a movie once.
    [Fact]
    public async Task ShelveAsync_SameTmdbMoviePickedTwice_SavesItOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var intake = Intake(rewind);
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);
        var again = await PickedAsync(intake, TmdbJson.HalloweenId);

        var shelving = await intake.ShelveAsync(Label, FactoryUpc, [halloween, again], ct);

        Assert.Equal(ShelveStatus.Shelved, shelving.Status);
        await using var db = rewind.OpenDb();
        Assert.Equal(1, await db.Movies.CountAsync(ct));
        Assert.Equal(1, await db.CaseMovies.CountAsync(ct));
    }

    [Fact]
    public async Task ShelveAsync_SameOwnedMoviePickedTwice_LinksItOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloweenId = await AddHalloweenAsync(rewind, ct);
        var intake = Intake(rewind);
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);
        var again = await PickedAsync(intake, TmdbJson.HalloweenId);

        var shelving = await intake.ShelveAsync(Label, FactoryUpc, [halloween, again], ct);

        Assert.Equal(ShelveStatus.Shelved, shelving.Status);
        await using var db = rewind.OpenDb();
        var shelvedId = await db.Cases.Where(c => c.StoreLabel == Label).Select(c => c.Id).SingleAsync(ct);
        Assert.Equal([halloweenId], await db.CaseMovies.Where(cm => cm.CaseId == shelvedId).Select(cm => cm.MovieId).ToListAsync(ct));
    }

    // Only a TMDB ID says two of a case's movies are one. Two typed in by hand with one title, such
    // as two volumes of the same home movies, are two movies.
    [Fact]
    public async Task ShelveAsync_SameTitleTypedTwice_SavesTwoMovies()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var intake = Intake(rewind);
        var volumeOne = Assert.IsType<CaseMovieEntry.ByHand>(intake.EnterByHand("The Haddonfield Story", "", "", "").Entry);
        var volumeTwo = Assert.IsType<CaseMovieEntry.ByHand>(intake.EnterByHand("The Haddonfield Story", "", "", "").Entry);

        await intake.ShelveAsync(Label, null, [volumeOne, volumeTwo], ct);

        await using var db = rewind.OpenDb();
        Assert.Equal(2, await db.Movies.CountAsync(ct));
        Assert.Equal(2, await db.CaseMovies.CountAsync(ct));
    }

    // DATA-3: a movie typed in by hand has no TMDB ID or fetch time, and what TMDB would have given
    // or the clerk left blank is NULL.
    [Fact]
    public async Task ShelveAsync_ByHandMovie_SavesNullTmdbIdAndFetchedAt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var intake = Intake(rewind);
        var typed = Assert.IsType<CaseMovieEntry.ByHand>(intake.EnterByHand("The Haddonfield Story", "1985", "", "").Entry);

        await intake.ShelveAsync(Label, null, [typed], ct);

        await using var db = rewind.OpenDb();
        var movie = await db.Movies.AsNoTracking().Include(m => m.Cast).Include(m => m.Genres).SingleAsync(ct);
        Assert.Equal(("The Haddonfield Story", 1985), (movie.Title, movie.Year));
        Assert.Null(movie.TmdbId);
        Assert.Null(movie.TmdbFetchedAt);
        Assert.Equal((null, null, null, null, null), (movie.RuntimeMinutes, movie.Rating, movie.Director, movie.Overview, movie.PosterFile));
        Assert.Empty(movie.Cast);
        Assert.Empty(movie.Genres);
    }

    // DATA-3 end to end: TMDB's 0 and "" become NULL, which the database's checks would refuse otherwise.
    [Fact]
    public async Task ShelveAsync_UnknownRuntimeAndRating_SavesNulls()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        _tmdb.AnswerJson(
            FakeTmdb.MovieFor(TmdbJson.HalloweenStoreId),
            TmdbJson.Movie(title: "Halloween Store", releaseDate: "", runtime: 0, overview: "", posterPath: null, releases: [("US", "")]));
        var intake = Intake(rewind);
        var store = await PickedAsync(intake, TmdbJson.HalloweenStoreId);

        var shelving = await intake.ShelveAsync(Label, FactoryUpc, [store], ct);

        Assert.Equal(ShelveStatus.Shelved, shelving.Status);
        await using var db = rewind.OpenDb();
        var movie = await db.Movies.AsNoTracking().SingleAsync(ct);
        Assert.Equal((null, null, null, null, null), (movie.Year, movie.RuntimeMinutes, movie.Rating, movie.Overview, movie.PosterFile));
    }

    // GEN-7: a poster that can't be saved, such as when the data folder's permissions are wrong,
    // doesn't keep the case off the shelf, and the other size is still saved.
    [Fact]
    public async Task ShelveAsync_PosterCantBeSaved_StillShelves()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var intake = Intake(rewind);
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);

        // A file where the posters folder should be: creating the folder fails, on Windows and on Linux.
        await File.WriteAllTextAsync(rewind.Paths.Posters, "", ct);

        var shelving = await intake.ShelveAsync(Label, FactoryUpc, [halloween], ct);

        Assert.Equal(ShelveStatus.Shelved, shelving.Status);
        Assert.Equal(ThumbPoster, await File.ReadAllBytesAsync(Path.Combine(rewind.Paths.Thumbs, "948.jpg"), ct));
        await using var db = rewind.OpenDb();
        Assert.Equal("948.jpg", await db.Movies.Select(m => m.PosterFile).SingleAsync(ct));
    }

    // GEN-7: a poster that wouldn't download at the pick doesn't keep the case off the shelf. Its
    // movie is saved without the file, and PosterFile still names it, so the TMDB refresh (Build
    // Plan 4.5) can download it, as after a restore.
    [Fact]
    public async Task ShelveAsync_PosterWontDownload_ShelvesTheMovieWithoutIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        _tmdb.Throw(FakeTmdb.PosterFor("w342", TmdbJson.HalloweenPoster), new HttpRequestException("No such host is known."));
        _tmdb.Throw(FakeTmdb.PosterFor("w154", TmdbJson.HalloweenPoster), new HttpRequestException("No such host is known."));
        var intake = Intake(rewind);
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);

        var shelving = await intake.ShelveAsync(Label, FactoryUpc, [halloween], ct);

        Assert.Equal(ShelveStatus.Shelved, shelving.Status);
        await using var db = rewind.OpenDb();
        Assert.Equal("948.jpg", await db.Movies.Select(m => m.PosterFile).SingleAsync(ct));
        Assert.False(Directory.Exists(rewind.Paths.Posters));
        Assert.False(Directory.Exists(rewind.Paths.Thumbs));
    }

    // NET-12: once the case is saved, a cancel can't make it look unsaved. The clerk leaves the
    // screen the moment the save commits, and the case still comes back on the shelf, with its
    // posters, rather than as an exception for a case whose label would then be in use.
    [Fact]
    public async Task ShelveAsync_CancelledOnceSaved_StillShelvesWithItsPosters()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        using var leaving = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var intake = Intake(rewind, dbFactory: new LeftJustAfterEachSave(rewind, leaving));
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);

        var shelving = await intake.ShelveAsync(Label, FactoryUpc, [halloween], leaving.Token);

        Assert.Equal(ShelveStatus.Shelved, shelving.Status);
        Assert.Equal(RegisterPoster, await File.ReadAllBytesAsync(Path.Combine(rewind.Paths.Posters, "948.jpg"), ct));
        Assert.Equal(ThumbPoster, await File.ReadAllBytesAsync(Path.Combine(rewind.Paths.Thumbs, "948.jpg"), ct));
    }

    [Fact]
    public async Task ShelveAsync_NoMovies_SavesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var shelving = await Intake(rewind).ShelveAsync(Label, FactoryUpc, [], ct);

        Assert.Equal(new Shelving(ShelveStatus.NoMovies, null), shelving);
        await using var db = rewind.OpenDb();
        Assert.Equal(0, await db.Cases.CountAsync(ct));
    }

    // SEC-5: only a store label goes on a case. The database's CHECK would throw instead.
    [Theory]
    [InlineData("RV-0042")]
    [InlineData("MEM-001")]
    [InlineData(FactoryUpc)]
    [InlineData("")]
    public async Task ShelveAsync_NotALabel_SavesNothing(string storeLabel)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var intake = Intake(rewind);
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);

        var shelving = await intake.ShelveAsync(storeLabel, FactoryUpc, [halloween], ct);

        Assert.Equal(new Shelving(ShelveStatus.NotALabel, null), shelving);
        await using var db = rewind.OpenDb();
        Assert.Equal(0, await db.Movies.CountAsync(ct));
    }

    // SEC-5: a factory barcode is 12 or 13 digits with a check digit that adds up.
    [Theory]
    [InlineData("03600029145")] // 11 digits
    [InlineData("036000291453")] // the wrong check digit
    [InlineData(Label)]
    public async Task ShelveAsync_NotAUpc_SavesNothing(string factoryUpc)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var intake = Intake(rewind);
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);

        var shelving = await intake.ShelveAsync(Label, factoryUpc, [halloween], ct);

        Assert.Equal(new Shelving(ShelveStatus.NotAUpc, null), shelving);
        await using var db = rewind.OpenDb();
        Assert.Equal(0, await db.Cases.CountAsync(ct));
    }

    // A DVD whose case has no barcode, its title typed in: the case has no factory UPC.
    [Fact]
    public async Task ShelveAsync_NoBarcode_SavesNoUpc()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var intake = Intake(rewind);
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);

        await intake.ShelveAsync(Label, null, [halloween], ct);

        await using var db = rewind.OpenDb();
        Assert.Null(await db.Cases.Select(c => c.FactoryUpc).SingleAsync(ct));
    }

    // The phone catalog matches barcodes in Rewind's form, so a 13-digit EAN that starts with 0 is
    // saved as its 12-digit UPC-A.
    [Fact]
    public async Task ShelveAsync_EanForAUpcA_SavesTheUpcA()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var intake = Intake(rewind);
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);

        await intake.ShelveAsync(Label, "0" + FactoryUpc, [halloween], ct);

        await using var db = rewind.OpenDb();
        Assert.Equal(FactoryUpc, await db.Cases.Select(c => c.FactoryUpc).SingleAsync(ct));
    }

    // The screen's line: which label went on the shelf, and each movie in its case once.
    [Fact]
    public async Task ShelveAsync_Shelved_NamesTheLabelAndEachMovie()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        AnswerHalloween2018();
        var intake = Intake(rewind);
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);
        var sequel = await PickedAsync(intake, TmdbJson.Halloween2018Id);

        var shelving = await intake.ShelveAsync(Label, FactoryUpc, [halloween, sequel, halloween], ct);

        Assert.Equal(Label, shelving.Case?.StoreLabel);
        Assert.Equal([new ShelvedMovie("Halloween", 1978), new ShelvedMovie("Halloween", 2018)], shelving.Case?.Movies);
    }

    // OBS-1, OBS-3: each case that goes on the shelf is a line in the log, with its label and movies.
    [Fact]
    public async Task ShelveAsync_Shelved_LogsTheLabelAndMovies()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        AnswerHalloween2018();
        var log = new ListLogger<MovieIntake>();
        var intake = Intake(rewind, logger: log);
        var halloween = await PickedAsync(intake, TmdbJson.HalloweenId);
        var sequel = await PickedAsync(intake, TmdbJson.Halloween2018Id);

        await intake.ShelveAsync(Label, FactoryUpc, [halloween, sequel], ct);

        Assert.Contains(
            (LogLevel.Information, "Put case RV-00042 on the shelf, holding Halloween (1978), Halloween (2018)", (Exception?)null),
            log.Entries);
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

    // Made once a test's database is ready, and once only in each test: TmdbClient sets up the
    // HttpClient it's given, which can't change once it has sent a request.
    private MovieIntake Intake(
        TempRewind rewind,
        string token = Token,
        IDbContextFactory<RewindDb>? dbFactory = null,
        ILogger<MovieIntake>? logger = null) =>
        new(
            new TmdbClient(_http, Options.Create(new TmdbOptions { AccessToken = token }), NullLogger<TmdbClient>.Instance),
            new Posters(Options.Create(rewind.Paths), NullLogger<Posters>.Instance),
            dbFactory ?? rewind.DbFactory,
            rewind.Time,
            logger ?? NullLogger<MovieIntake>.Instance);

    // For EnterByHand, which touches nothing: a database that throws if it's opened, and a posters
    // folder that's never made.
    private MovieIntake HandEntryIntake() =>
        new(
            new TmdbClient(_http, Options.Create(new TmdbOptions { AccessToken = Token }), NullLogger<TmdbClient>.Instance),
            new Posters(Options.Create(new RewindPaths { Root = Path.Combine(Path.GetTempPath(), $"rewind-test-{Guid.NewGuid():N}") }), NullLogger<Posters>.Instance),
            new NoDatabase(),
            _registerClock,
            NullLogger<MovieIntake>.Instance);

    // The clerk's pick, as the screen holds it until the label scan.
    private static async Task<CaseMovieEntry> PickedAsync(MovieIntake intake, int tmdbId) =>
        Assert.IsAssignableFrom<CaseMovieEntry>((await intake.PickAsync(tmdbId, TestContext.Current.CancellationToken)).Entry);

    // Halloween (2018), for a second movie in a case: Horror and Thriller too, with no poster.
    private void AnswerHalloween2018() =>
        _tmdb.AnswerJson(FakeTmdb.MovieFor(TmdbJson.Halloween2018Id), TmdbJson.Movie(releaseDate: "2018-10-18", posterPath: null));

    // Halloween (1978) in the collection already, as an earlier intake saved it, in the case of
    // another edition, on the shelf. Its cast goes in bottom of the bill first, and its genres
    // Thriller first, so a read that doesn't put them in order gets them backwards.
    private static async Task<int> AddHalloweenAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        var halloween = new Movie
        {
            TmdbId = TmdbJson.HalloweenId,
            Title = "Halloween",
            Year = 1978,
            RuntimeMinutes = 91,
            Rating = "R",
            Director = "John Carpenter",
            Genres = { new Genre { TmdbId = TmdbJson.ThrillerId, Name = "Thriller" }, new Genre { TmdbId = TmdbJson.HorrorId, Name = "Horror" } },
        };
        for (var place = TopFive.Length - 1; place >= 0; place--)
        {
            halloween.Cast.Add(new CastMember { Name = TopFive[place], BillingOrder = place });
        }

        db.Cases.Add(new Case { StoreLabel = "RV-00001", FactoryUpc = FirstEditionUpc, Movies = { halloween } });
        await db.SaveChangesAsync(cancellationToken);
        return halloween.Id;
    }

    // Another case has the label already.
    private static async Task TakeTheLabelAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        db.Cases.Add(new Case { StoreLabel = Label });
        await db.SaveChangesAsync(cancellationToken);
    }

    private sealed class NoDatabase : IDbContextFactory<RewindDb>
    {
        public RewindDb CreateDbContext() => throw new InvalidOperationException("EnterByHand opened the database.");
    }

    // Contexts each of whose saves another case beats to the label: just before the save, a case
    // labeled RV-00042 is saved through a context of its own.
    private sealed class LabelTakenJustBeforeEachSave(TempRewind rewind) : IDbContextFactory<RewindDb>
    {
        public RewindDb CreateDbContext() =>
            new(new DbContextOptionsBuilder<RewindDb>()
                .UseSqlite(RewindDb.ConnectionStringFor(rewind.Paths.Database))
                .AddInterceptors(new TakeTheLabelFirst(rewind))
                .Options);
    }

    private sealed class TakeTheLabelFirst(TempRewind rewind) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await TakeTheLabelAsync(rewind, cancellationToken);
            return result;
        }
    }

    // Contexts that cancel the clerk's token the moment a save commits, as if the clerk left the
    // screen just then.
    private sealed class LeftJustAfterEachSave(TempRewind rewind, CancellationTokenSource leaving) : IDbContextFactory<RewindDb>
    {
        public RewindDb CreateDbContext() =>
            new(new DbContextOptionsBuilder<RewindDb>()
                .UseSqlite(RewindDb.ConnectionStringFor(rewind.Paths.Database))
                .AddInterceptors(new LeaveOnceSaved(leaving))
                .Options);
    }

    private sealed class LeaveOnceSaved(CancellationTokenSource leaving) : SaveChangesInterceptor
    {
        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            await leaving.CancelAsync();
            return result;
        }
    }

    // Contexts that write EF Core's own log to a list, at every level. One logger factory serves
    // them all, as one serves the register, so EF Core builds its services once.
    private sealed class LoggedContexts(TempRewind rewind, ListLoggerProvider log) : IDbContextFactory<RewindDb>, IDisposable
    {
        private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(log));

        public RewindDb CreateDbContext() =>
            new(new DbContextOptionsBuilder<RewindDb>()
                .UseSqlite(RewindDb.ConnectionStringFor(rewind.Paths.Database))
                .UseLoggerFactory(_loggerFactory)
                .Options);

        public void Dispose() => _loggerFactory.Dispose();
    }
}
