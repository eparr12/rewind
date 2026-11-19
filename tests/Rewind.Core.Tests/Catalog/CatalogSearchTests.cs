using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rewind.Core.Catalog;
using Rewind.Core.Data;
using Rewind.Core.Metadata;

namespace Rewind.Core.Tests.Catalog;

public sealed class CatalogSearchTests
{
    // TMDB's IDs, which name the thumbnails (SEC-5).
    private const int HalloweenId = 948;
    private const int JawsId = 578;
    private const int TrueLiesId = 36955;

    // The same length, so only their bytes tell them apart.
    private static readonly byte[] HalloweenThumb = [0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x54];
    private static readonly byte[] HalloweenPoster = [0xFF, 0xD8, 0xFF, 0xE0, 0x03, 0x42];

    // Build Plan 4.1's Done when. True Lies is out tonight, so it's found but not on the shelf.
    [Fact]
    public async Task SearchAsync_ActorsName_FindsTheirMoviesWithTheOneOnTheShelfMarked()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(
            rewind,
            ct,
            new Case { StoreLabel = "RV-00001", Movies = { Halloween() } },
            new Case { StoreLabel = "RV-00002", Status = CaseStatus.CheckedOut, Movies = { TrueLies() } },
            new Case { StoreLabel = "RV-00003", Movies = { Jaws() } });

        var found = await SearchFor(rewind).SearchAsync("Jamie Lee Curtis", null, null, null, ct);

        Assert.Equal(SearchStatus.Searched, found.Status);
        Assert.Equal([("Halloween", true), ("True Lies", false)], found.Hits.Select(hit => (hit.Title, hit.OnShelf)));
    }

    // Every field of the hit. Jaws is saved first, so Halloween's Id isn't 1, and it has no thumbnail saved.
    [Fact]
    public async Task SearchAsync_Movie_GivesItsIdTitleYearRatingAndShelf()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloween = Halloween();
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { Jaws() } }, new Case { StoreLabel = "RV-00002", Movies = { halloween } });

        var found = await SearchFor(rewind).SearchAsync("Halloween", null, null, null, ct);

        Assert.Equal(new MovieHit(halloween.Id, "Halloween", 1978, "R", OnShelf: true, Thumb: null), Assert.Single(found.Hits));
    }

    // Part of a word from the middle, so a match only at the start or the end isn't enough, and in capitals, so an
    // exact match of letter case isn't either: of Halloween's title, its director's name and a cast member's.
    [Theory]
    [InlineData("ALLOWE")]
    [InlineData("CARPENT")]
    [InlineData("PLEAS")]
    public async Task SearchAsync_PartOfATitleADirectorOrACastMember_FindsTheMovie(string text)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { Jaws(), Halloween(), TrueLies() } });

        var found = await SearchFor(rewind).SearchAsync(text, null, null, null, ct);

        Assert.Equal(["Halloween (1978)"], TitlesOf(found));
    }

    // Null, blank or only white space in every box is nothing typed or picked.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task SearchAsync_NothingTypedOrPicked_ListsTheWholeCatalog(string? blank)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var horror = new Genre { TmdbId = 27, Name = "Horror" };
        var halloween = Halloween();
        halloween.Genres.Add(horror);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { TrueLies(), Jaws(), halloween } });

        var found = await SearchFor(rewind).SearchAsync(blank, blank, blank, blank, ct);

        Assert.Equal(["Halloween (1978)", "Jaws (1975)", "True Lies (1994)"], TitlesOf(found));
    }

    // Cleaned as a title typed in by hand is: a line break, such as one pasted in, is a space between the words.
    [Fact]
    public async Task SearchAsync_TextWithALineBreak_ReadsItAsASpace()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { Jaws(), Halloween(), TrueLies() } });

        var found = await SearchFor(rewind).SearchAsync("Jamie\nLee", null, null, null, ct);

        Assert.Equal(["Halloween (1978)", "True Lies (1994)"], TitlesOf(found));
    }

    // SEC-5: capped as a title typed in by hand is, so what's typed past the cap isn't looked for.
    [Fact]
    public async Task SearchAsync_TextPastTheCap_IsntLookedFor()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var longest = new string('A', TmdbClient.MaxNameLength);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { new Movie { Title = longest } } });

        var found = await SearchFor(rewind).SearchAsync(longest + " and more", null, null, null, ct);

        Assert.Equal([longest], found.Hits.Select(hit => hit.Title));
    }

    // DATA-13: each is matched as itself. As wildcards, 100% would find 1001 Nights too, and e_N File Name too. A
    // backslash left as the escape character would escape the pattern's closing %, and find only titles ending in %.
    [Theory]
    [InlineData("100%", "100% Wolf")]
    [InlineData("e_N", "File_Name")]
    [InlineData(@"\", @"Back\Slash")]
    public async Task SearchAsync_TypedWildcard_IsMatchedAsItself(string text, string title)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(
            rewind,
            ct,
            new Case
            {
                StoreLabel = "RV-00001",
                Movies =
                {
                    new Movie { Title = "100% Wolf" },
                    new Movie { Title = "1001 Nights" },
                    new Movie { Title = "File_Name" },
                    new Movie { Title = "File Name" },
                    new Movie { Title = @"Back\Slash" },
                },
            });

        var found = await SearchFor(rewind).SearchAsync(text, null, null, null, ct);

        Assert.Equal([title], found.Hits.Select(hit => hit.Title));
    }

    // The known limit, #100: SQLite's LIKE folds letter case for A to Z only, and never folds an accent. So Amélie is
    // found only when its é is typed as it is: the case of its other letters doesn't matter, but e and É aren't é.
    [Theory]
    [InlineData("amélie", true)]
    [InlineData("amelie", false)]
    [InlineData("AMÉLIE", false)]
    public async Task SearchAsync_AccentedTitle_IsFoundOnlyWithTheAccentInItsOwnCase(string text, bool isFound)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { new Movie { Title = "Amélie", Year = 2001 } } });

        var found = await SearchFor(rewind).SearchAsync(text, null, null, null, ct);

        string[] expected = isFound ? ["Amélie (2001)"] : [];
        Assert.Equal(expected, TitlesOf(found));
    }

    // Each filter alone narrows the whole catalog. The rating is read as a typed one is, so " pg " is PG.
    [Theory]
    [InlineData("Comedy", null, null, new[] { "Goosebumps 2: Haunted Halloween (2018)", "Halloween at the Hotel (2018)" })]
    [InlineData(null, "1978", null, new[] { "Halloween (1978)" })]
    [InlineData(null, null, " pg ", new[] { "Goosebumps 2: Haunted Halloween (2018)" })]
    public async Task SearchAsync_OneFilter_FindsWhatMatchesIt(string? genre, string? year, string? rating, string[] expected)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddHalloweenNightAsync(rewind, ct);

        var found = await SearchFor(rewind).SearchAsync(null, genre, year, rating, ct);

        Assert.Equal(expected, TitlesOf(found));
    }

    // The text alone finds four of the five, and each filter narrows those.
    [Theory]
    [InlineData("Horror", null, null, new[] { "Goosebumps 2: Haunted Halloween (2018)", "Halloween (1978)", "Halloween (2018)" })]
    [InlineData(null, "2018", null, new[] { "Goosebumps 2: Haunted Halloween (2018)", "Halloween (2018)", "Halloween at the Hotel (2018)" })]
    [InlineData(null, null, "R", new[] { "Halloween (1978)", "Halloween (2018)", "Halloween at the Hotel (2018)" })]
    public async Task SearchAsync_OneFilterAndText_FindsWhatMatchesBoth(string? genre, string? year, string? rating, string[] expected)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddHalloweenNightAsync(rewind, ct);

        var found = await SearchFor(rewind).SearchAsync("Halloween", genre, year, rating, ct);

        Assert.Equal(expected, TitlesOf(found));
    }

    // Each of the others misses just one of the text, the genre, the year and the rating, so a search that left any
    // one of them out would find that movie too.
    [Fact]
    public async Task SearchAsync_EveryFilterAndText_FindsWhatMatchesThemAll()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddHalloweenNightAsync(rewind, ct);

        var found = await SearchFor(rewind).SearchAsync("Halloween", "Horror", "2018", "R", ct);

        Assert.Equal(["Halloween (2018)"], TitlesOf(found));
    }

    // DATA-3: Home Movies' year, rating and genres are all unknown, so no filter matches it, and each still finds
    // Halloween.
    [Theory]
    [InlineData("Horror", null, null)]
    [InlineData(null, "1978", null)]
    [InlineData(null, null, "R")]
    public async Task SearchAsync_FilterOnWhatsUnknown_NeverMatchesIt(string? genre, string? year, string? rating)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloween = Halloween();
        halloween.Genres.Add(new Genre { TmdbId = 27, Name = "Horror" });
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { new Movie { Title = "Home Movies" }, halloween } });

        var found = await SearchFor(rewind).SearchAsync(null, genre, year, rating, ct);

        Assert.Equal(["Halloween (1978)"], TitlesOf(found));
    }

    // Read as EnterByHand reads a year, before the database is asked: NoDatabase throws if it's opened. Next year on
    // the register's calendar is 2027, so 2028 is a slip.
    [Theory]
    [InlineData("nineteen seventy-eight")]
    [InlineData("78")]
    [InlineData("2028")]
    public async Task SearchAsync_YearThatIsntOne_SaysSoAndAsksTheDatabaseNothing(string year)
    {
        await using var rewind = new TempRewind();

        var found = await SearchFor(rewind, new NoDatabase("The catalog search"))
            .SearchAsync("Halloween", null, year, null, TestContext.Current.CancellationToken);

        Assert.Equal((SearchStatus.NotAYear, 0, false), (found.Status, found.Hits.Count, found.MoreMatched));
    }

    // DATA-3: NR is TMDB's "No rating information", which Rewind keeps as unknown, so no movie is rated NR.
    [Theory]
    [InlineData("NR")]
    [InlineData("PG13")]
    [InlineData("TV-14")]
    public async Task SearchAsync_RatingThatIsntOne_SaysSoAndAsksTheDatabaseNothing(string rating)
    {
        await using var rewind = new TempRewind();

        var found = await SearchFor(rewind, new NoDatabase("The catalog search"))
            .SearchAsync("Halloween", null, null, rating, TestContext.Current.CancellationToken);

        Assert.Equal((SearchStatus.NotARating, 0, false), (found.Status, found.Hits.Count, found.MoreMatched));
    }

    // A movie whose only case is retired isn't ours any more.
    [Fact]
    public async Task SearchAsync_MovieWhoseOnlyCaseIsRetired_LeavesItOut()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(
            rewind,
            ct,
            new Case { StoreLabel = "RV-00001", Status = CaseStatus.Retired, Movies = { Jaws() } },
            new Case { StoreLabel = "RV-00002", Movies = { Halloween() } });

        var found = await SearchFor(rewind).SearchAsync(null, null, null, null, ct);

        Assert.Equal(["Halloween (1978)"], TitlesOf(found));
    }

    [Fact]
    public async Task SearchAsync_MovieInNoCase_LeavesItOut()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, Jaws(), new Case { StoreLabel = "RV-00001", Movies = { Halloween() } });

        var found = await SearchFor(rewind).SearchAsync(null, null, null, null, ct);

        Assert.Equal(["Halloween (1978)"], TitlesOf(found));
    }

    [Fact]
    public async Task SearchAsync_MovieWithARetiredCaseAndOneOnTheShelf_IsOnTheShelf()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var jaws = Jaws();
        await AddInOrderAsync(
            rewind,
            ct,
            new Case { StoreLabel = "RV-00001", Status = CaseStatus.Retired, Movies = { jaws } },
            new Case { StoreLabel = "RV-00002", Movies = { jaws } });

        var found = await SearchFor(rewind).SearchAsync("Jaws", null, null, null, ct);

        Assert.True(Assert.Single(found.Hits).OnShelf);
    }

    // Such as two editions, one of them out tonight.
    [Fact]
    public async Task SearchAsync_MovieInTwoCases_IsOneHit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var jaws = Jaws();
        await AddInOrderAsync(
            rewind,
            ct,
            new Case { StoreLabel = "RV-00001", Movies = { jaws } },
            new Case { StoreLabel = "RV-00002", Status = CaseStatus.CheckedOut, Movies = { jaws } });

        var found = await SearchFor(rewind).SearchAsync("Jaws", null, null, null, ct);

        Assert.Equal(["Jaws (1975)"], TitlesOf(found));
    }

    // Arnold Schwarzenegger and Tom Arnold are both in True Lies.
    [Fact]
    public async Task SearchAsync_TextInTwoCastMembersNames_FindsTheirMovieOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { TrueLies() } });

        var found = await SearchFor(rewind).SearchAsync("Arnold", null, null, null, ct);

        Assert.Equal(["True Lies (1994)"], TitlesOf(found));
    }

    [Fact]
    public async Task SearchAsync_NothingTyped_ListsByTitleThenYearThenId()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var inOrder = await AddSixToSortAsync(rewind, ct);

        var found = await SearchFor(rewind).SearchAsync(null, null, null, null, ct);

        Assert.Equal(inOrder, found.Hits.Select(hit => hit.MovieId));
    }

    // TEST-4: SQLite never promised the order it reads rows in, so these contexts read them backwards, which SQLite does
    // only for a read with no ORDER BY. So this catches an order the code puts on the rows after the read, such as a
    // sort without the Id, which would give the two movies that share a title and a year the wrong way round. SQLite's
    // own ORDER BY gives tied rows in the order it reads them, Id order, either way, so no test here can show the Id
    // left out of it.
    [Fact]
    public async Task SearchAsync_NothingTypedReadBackwards_ListsInTheSameOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var inOrder = await AddSixToSortAsync(rewind, ct);

        var found = await SearchFor(rewind, rewind.ContextsWith(new BackwardsReads(), pooled: false)).SearchAsync(null, null, null, null, ct);

        Assert.Equal(inOrder, found.Hits.Select(hit => hit.MovieId));
    }

    // NET-12: the clerk sees the first in order, and is told more matched, so they can narrow the search.
    [Fact]
    public async Task SearchAsync_MoreMatchesThanItGives_GivesTheFirstAndSaysMoreMatched()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddNumberedAsync(rewind, CatalogSearch.MaxHits + 1, ct);

        var found = await SearchFor(rewind).SearchAsync(null, null, null, null, ct);

        Assert.Equal(NumberedTitles(CatalogSearch.MaxHits), found.Hits.Select(hit => hit.Title));
        Assert.True(found.MoreMatched);
    }

    [Fact]
    public async Task SearchAsync_AsManyMatchesAsItGives_GivesThemAllAndSaysNoMoreMatched()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddNumberedAsync(rewind, CatalogSearch.MaxHits, ct);

        var found = await SearchFor(rewind).SearchAsync(null, null, null, null, ct);

        Assert.Equal(NumberedTitles(CatalogSearch.MaxHits), found.Hits.Select(hit => hit.Title));
        Assert.False(found.MoreMatched);
    }

    // The thumbnail, not the register's bigger poster, which is saved too.
    [Fact]
    public async Task SearchAsync_MovieWithASavedThumbnail_GivesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { Halloween() } });
        await SaveAsync(rewind.Paths.Thumbs, HalloweenId, HalloweenThumb, ct);
        await SaveAsync(rewind.Paths.Posters, HalloweenId, HalloweenPoster, ct);

        var found = await SearchFor(rewind).SearchAsync("Halloween", null, null, null, ct);

        Assert.Equal(HalloweenThumb, Assert.Single(found.Hits).Thumb);
    }

    // GEN-7: such as a movie whose poster never downloaded, or one not downloaded again since a restore.
    [Fact]
    public async Task SearchAsync_MovieWithNoSavedThumbnail_GivesItWithoutOne()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { Halloween() } });

        var found = await SearchFor(rewind).SearchAsync("Halloween", null, null, null, ct);

        Assert.Null(Assert.Single(found.Hits).Thumb);
    }

    // GEN-7: a folder where the thumbnail should be can't be read, on Windows or on Linux. Posters says why in the log
    // (OBS-4).
    [Fact]
    public async Task SearchAsync_ThumbnailThatCantBeRead_GivesTheMovieWithoutItAndWarns()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { Halloween() } });
        Directory.CreateDirectory(Path.Combine(rewind.Paths.Thumbs, $"{HalloweenId}.jpg"));
        var log = new ListLogger<Posters>();

        var found = await SearchFor(rewind, postersLog: log).SearchAsync("Halloween", null, null, null, ct);

        Assert.Null(Assert.Single(found.Hits).Thumb);
        Assert.Equal(LogLevel.Warning, Assert.Single(log.Entries).Level);
    }

    // A movie entered by hand has no TMDB ID to name a thumbnail.
    [Fact]
    public async Task SearchAsync_MovieEnteredByHand_GivesItWithoutAThumbnail()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { new Movie { Title = "Home Movies" } } });

        var found = await SearchFor(rewind).SearchAsync("Home Movies", null, null, null, ct);

        Assert.Null(Assert.Single(found.Hits).Thumb);
    }

    // NET-7.
    [Fact]
    public async Task SearchAsync_Cancelled_Throws()
    {
        await using var rewind = new TempRewind();
        await rewind.StartAsync(TestContext.Current.CancellationToken);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SearchFor(rewind).SearchAsync("Halloween", null, null, null, cancelled.Token));
    }

    // NET-7: cancelled once the database has answered, so the thumbnail's read is what hears it.
    [Fact]
    public async Task SearchAsync_CancelledOnceTheDatabaseAnswered_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { Halloween() } });
        await SaveAsync(rewind.Paths.Thumbs, HalloweenId, HalloweenThumb, ct);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => SearchFor(rewind, rewind.ContextsWith(new CancelAfterTheRead(cancel))).SearchAsync("Halloween", null, null, null, cancel.Token));

        Assert.True(cancel.IsCancellationRequested); // the interceptor fired
    }

    // The register's log keeps Microsoft's warnings, so a warning EF Core gave for the search would land in it after
    // every start, for nothing that's wrong. Here each warning throws, in contexts that compile the read afresh (see
    // EveryWarningThrows).
    [Fact]
    public async Task SearchAsync_EveryFilterAndText_TripsNoEfCoreWarning()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddHalloweenNightAsync(rewind, ct);

        var failure = await Record.ExceptionAsync(() => SearchFor(rewind, EveryWarningThrows(rewind)).SearchAsync("Halloween", "Horror", "2018", "R", ct));

        Assert.Null(failure);
    }

    // DATA-12: one statement, so the hits, whether each is on the shelf and whether more matched all come from one
    // moment. EF Core logs each command it runs.
    [Fact]
    public async Task SearchAsync_EveryFilterAndText_AsksTheDatabaseOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddHalloweenNightAsync(rewind, ct);
        var log = new ListLoggerProvider();
        using var logged = new LoggedContexts(rewind, log);

        await SearchFor(rewind, logged).SearchAsync("Halloween", "Horror", "2018", "R", ct);

        Assert.Single(log.Entries, entry => entry.Message.StartsWith("Executed DbCommand", StringComparison.Ordinal));
    }

    // Only a listed movie's genres. True Lies is out tonight, so it's listed, but Tombstone's only case is retired,
    // Rocky is in no case, and no movie is a comedy. Halloween and Jaws are both horror, which is listed once.
    [Fact]
    public async Task GenresAsync_Collection_ListsEachListedMoviesGenresOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var horror = new Genre { TmdbId = 27, Name = "Horror" };
        var halloween = Halloween();
        halloween.Genres.Add(horror);
        var jaws = Jaws();
        jaws.Genres.Add(horror);
        jaws.Genres.Add(new Genre { TmdbId = 53, Name = "Thriller" });
        var trueLies = TrueLies();
        trueLies.Genres.Add(new Genre { TmdbId = 28, Name = "Action" });
        await AddInOrderAsync(
            rewind,
            ct,
            new Genre { TmdbId = 35, Name = "Comedy" },
            new Movie { Title = "Rocky", Year = 1976, Genres = { new Genre { TmdbId = 18, Name = "Drama" } } },
            new Case { StoreLabel = "RV-00001", Movies = { halloween } },
            new Case { StoreLabel = "RV-00002", Movies = { jaws } },
            new Case { StoreLabel = "RV-00003", Status = CaseStatus.CheckedOut, Movies = { trueLies } },
            new Case
            {
                StoreLabel = "RV-00004",
                Status = CaseStatus.Retired,
                Movies = { new Movie { Title = "Tombstone", Year = 1993, Genres = { new Genre { TmdbId = 37, Name = "Western" } } } },
            });

        var genres = await SearchFor(rewind).GenresAsync(ct);

        Assert.Equal(["Action", "Horror", "Thriller"], genres);
    }

    // Saved in the reverse of their order, so their Ids run against it. By character code, TV Movie would come before
    // Thriller.
    [Fact]
    public async Task GenresAsync_SeveralGenres_ListsThemAToZ()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var jaws = Jaws();
        jaws.Genres.Add(new Genre { TmdbId = 10770, Name = "TV Movie" });
        jaws.Genres.Add(new Genre { TmdbId = 53, Name = "Thriller" });
        jaws.Genres.Add(new Genre { TmdbId = 878, Name = "Science Fiction" });
        jaws.Genres.Add(new Genre { TmdbId = 27, Name = "Horror" });
        jaws.Genres.Add(new Genre { TmdbId = 28, Name = "Action" });
        await AddInOrderAsync(rewind, ct, [.. jaws.Genres, new Case { StoreLabel = "RV-00001", Movies = { jaws } }]);

        var genres = await SearchFor(rewind).GenresAsync(ct);

        Assert.Equal(["Action", "Horror", "Science Fiction", "Thriller", "TV Movie"], genres);
    }

    // Only their TMDB IDs are unique, so two genres can have one name but for its letter case. Saved in the reverse of
    // their order by character code, so their Ids run against it.
    [Fact]
    public async Task GenresAsync_NamesThatDifferOnlyInLetterCase_ListsThemByCharacterCode()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var lowerCase = new Genre { TmdbId = 99, Name = "horror" };
        var capital = new Genre { TmdbId = 27, Name = "Horror" };
        var jaws = Jaws();
        jaws.Genres.Add(lowerCase);
        jaws.Genres.Add(capital);
        await AddInOrderAsync(rewind, ct, lowerCase, capital, new Case { StoreLabel = "RV-00001", Movies = { jaws } });

        var genres = await SearchFor(rewind).GenresAsync(ct);

        Assert.Equal(["Horror", "horror"], genres);
    }

    // NET-7.
    [Fact]
    public async Task GenresAsync_Cancelled_Throws()
    {
        await using var rewind = new TempRewind();
        await rewind.StartAsync(TestContext.Current.CancellationToken);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SearchFor(rewind).GenresAsync(cancelled.Token));
    }

    // As for the search: each EF Core warning throws here, in contexts that compile the read afresh.
    [Fact]
    public async Task GenresAsync_Collection_TripsNoEfCoreWarning()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddHalloweenNightAsync(rewind, ct);

        var failure = await Record.ExceptionAsync(() => SearchFor(rewind, EveryWarningThrows(rewind)).GenresAsync(ct));

        Assert.Null(failure);
    }

    private static CatalogSearch SearchFor(TempRewind rewind, IDbContextFactory<RewindDb>? dbFactory = null, ILogger<Posters>? postersLog = null) =>
        new(dbFactory ?? rewind.DbFactory, new Posters(Options.Create(rewind.Paths), postersLog ?? NullLogger<Posters>.Instance), rewind.Time);

    // A new one for each test, so each database gets rows of its own.
    private static Movie Halloween() => new()
    {
        TmdbId = HalloweenId,
        Title = "Halloween",
        Year = 1978,
        Rating = "R",
        Director = "John Carpenter",
        Cast = { Actor("Donald Pleasence", 0), Actor("Jamie Lee Curtis", 1) },
    };

    private static Movie Jaws() => new()
    {
        TmdbId = JawsId,
        Title = "Jaws",
        Year = 1975,
        Rating = "PG",
        Director = "Steven Spielberg",
        Cast = { Actor("Roy Scheider", 0), Actor("Robert Shaw", 1) },
    };

    private static Movie TrueLies() => new()
    {
        TmdbId = TrueLiesId,
        Title = "True Lies",
        Year = 1994,
        Rating = "R",
        Director = "James Cameron",
        Cast = { Actor("Arnold Schwarzenegger", 0), Actor("Jamie Lee Curtis", 1), Actor("Tom Arnold", 2) },
    };

    private static CastMember Actor(string name, int billingOrder) => new() { Name = name, BillingOrder = billingOrder };

    // Halloween (2018) is the only one of these that has Halloween in its title, is a horror movie, came out in 2018
    // and is rated R. Each of the others misses just one of those.
    private static async Task AddHalloweenNightAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        var horror = new Genre { TmdbId = 27, Name = "Horror" };
        var comedy = new Genre { TmdbId = 35, Name = "Comedy" };
        await AddInOrderAsync(
            rewind,
            cancellationToken,
            new Case
            {
                StoreLabel = "RV-00001",
                Movies =
                {
                    Titled("Halloween", 2018, "R", horror),
                    Titled("Hereditary", 2018, "R", horror),
                    Titled("Halloween at the Hotel", 2018, "R", comedy),
                    Titled("Halloween", 1978, "R", horror),
                    Titled("Goosebumps 2: Haunted Halloween", 2018, "PG", comedy, horror),
                },
            });
    }

    private static Movie Titled(string title, int year, string rating, params Genre[] genres)
    {
        var movie = new Movie { Title = title, Year = year, Rating = rating };
        foreach (var genre in genres)
        {
            movie.Genres.Add(genre);
        }

        return movie;
    }

    // Saved in the reverse of the order they're listed in, so the Ids run against every part of it: the titles, read A
    // to Z whatever their letter case, so home movies comes between Halloween and Jaws; the years, unknown first; and
    // the two that share a title and a year. Returns their Ids in the order they're listed in.
    private static async Task<int[]> AddSixToSortAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        var jaws = Jaws();
        var homeMovies = new Movie { Title = "home movies" };
        var remake = new Movie { Title = "Halloween", Year = 2018 };
        var original = Halloween();
        var originalByHand = new Movie { Title = "Halloween", Year = 1978 };
        var yearUnknown = new Movie { Title = "Halloween" };
        await AddInOrderAsync(
            rewind,
            cancellationToken,
            jaws,
            homeMovies,
            remake,
            original,
            originalByHand,
            yearUnknown,
            new Case { StoreLabel = "RV-00001", Movies = { jaws, homeMovies, remake, original, originalByHand, yearUnknown } });
        return [yearUnknown.Id, original.Id, originalByHand.Id, remake.Id, homeMovies.Id, jaws.Id];
    }

    // A box set of Movie 01, Movie 02 and on, which sort as they're numbered.
    private static async Task AddNumberedAsync(TempRewind rewind, int count, CancellationToken cancellationToken)
    {
        var boxSet = new Case { StoreLabel = "RV-00001" };
        foreach (var title in NumberedTitles(count))
        {
            boxSet.Movies.Add(new Movie { Title = title });
        }

        await AddInOrderAsync(rewind, cancellationToken, boxSet);
    }

    private static List<string> NumberedTitles(int count) =>
        [.. Enumerable.Range(1, count).Select(number => "Movie " + number.ToString("D2", CultureInfo.InvariantCulture))];

    // Saves each row in a save of its own, in the order given, so their Ids come in that order too. One context saves
    // them all, so a movie or a genre in two of them is one row.
    private static async Task AddInOrderAsync(TempRewind rewind, CancellationToken cancellationToken, params object[] rows)
    {
        await using var db = rewind.OpenDb();
        foreach (var row in rows)
        {
            db.Add(row);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    // A poster or a thumbnail where Posters saves it, named for the movie's TMDB ID.
    private static async Task SaveAsync(string folder, int tmdbId, byte[] jpeg, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, $"{tmdbId}.jpg"), jpeg, cancellationToken);
    }

    // Each hit as the screens show a movie, such as "Halloween (1978)".
    private static List<string> TitlesOf(SearchResult found) => [.. found.Hits.Select(hit => Movie.TitleWithYear(hit.Title, hit.Year))];

    // Contexts in which every EF Core warning throws, rather than going to a log. EF Core compiles a query once for
    // every context that shares its internal services, and warns only then, so a read that another test compiled first
    // would never warn in a test's own log. Warnings set to throw are part of what EF Core builds those services for, so
    // these contexts get services of their own, and the read compiles here, whichever test ran first.
    private static IDbContextFactory<RewindDb> EveryWarningThrows(TempRewind rewind) =>
        rewind.ContextsWith(warnings: warnings => warnings.Default(WarningBehavior.Throw));
}
