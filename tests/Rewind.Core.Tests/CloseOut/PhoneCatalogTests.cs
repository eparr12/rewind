using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rewind.Core.Catalog;
using Rewind.Core.Checkout;
using Rewind.Core.CloseOut;
using Rewind.Core.Data;
using Rewind.Core.Returns;
using Rewind.Core.Tests.Checkout;

namespace Rewind.Core.Tests.CloseOut;

public sealed class PhoneCatalogTests
{
    private const string JawsUpc = "012345678905";
    private const string JawsSecondEditionUpc = "085391163121";
    private const string HalloweenUpc = "043396012356";
    private const string BoxSetUpc = "036000291452";
    private const string EuropeanUpc = "4006381333931"; // an EAN-13 that doesn't start with 0, so it stays 13 digits

    // TMDB's IDs, which name the thumbnails (SEC-5).
    private const int JawsId = 578;
    private const int HalloweenId = 948;
    private const int ScreamId = 4232;

    // The same length, so only their bytes tell them apart.
    private static readonly byte[] JawsThumb = [0xFF, 0xD8, 0xFF, 0xE0, 0x05, 0x78];
    private static readonly byte[] OtherThumb = [0xFF, 0xD8, 0xFF, 0xE0, 0x09, 0x48];

    // An outbox file's time from long before any test runs, to tell whether a write touched it.
    private static readonly DateTime LongAgo = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Build Plan 3.5: R6's format, parsed back. Close Out runs 17 hours and 7.5 seconds after TempRewind's movie
    // night, at 3:15 PM on the register, four hours behind UTC, so generatedAt is 19:15:07Z: in UTC, on a 24-hour
    // clock, and to the second.
    [Fact]
    public async Task WriteAsync_SmallCollection_WritesTheVersionTheTimeAndEachMovie()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var adventure = new Genre { TmdbId = 12, Name = "Adventure" };
        var horror = new Genre { TmdbId = 27, Name = "Horror" };
        var jaws = Jaws();
        jaws.Genres.Add(adventure);
        jaws.Genres.Add(horror);
        var halloween = Halloween();
        halloween.Genres.Add(horror);
        await AddInOrderAsync(
            rewind,
            ct,
            new Case { StoreLabel = "RV-00001", FactoryUpc = JawsUpc, Movies = { jaws } },
            new Case { StoreLabel = "RV-00002", FactoryUpc = HalloweenUpc, Movies = { halloween } });
        await SaveThumbAsync(rewind, JawsId, JawsThumb, ct);
        rewind.Time.Advance(new TimeSpan(17, 0, 7) + TimeSpan.FromMilliseconds(500));

        var written = await CatalogFor(rewind).WriteAsync(ct);

        var catalog = await ReadCatalogAsync(rewind, ct);
        Assert.Equal(2, written);
        Assert.Equal((1, "2026-10-24T19:15:07Z"), (catalog.GetProperty("version").GetInt32(), catalog.GetProperty("generatedAt").GetString()));
        Assert.Equal(
            [
                """{"title":"Halloween","year":1978,"rating":"R","minutes":91,"genres":["Horror"],"thumb":null,"upcs":["043396012356"]}""",
                """{"title":"Jaws","year":1975,"rating":"PG","minutes":124,"genres":["Adventure","Horror"],"thumb":"thumbs/578.jpg","upcs":["012345678905"]}""",
            ],
            MoviesIn(catalog));
    }

    // DATA-3: unknown is null, and a list with nothing in it is empty, never null. A movie entered by hand has no
    // TMDB ID, and so no thumbnail, and its case came with no barcode.
    [Fact]
    public async Task WriteAsync_MovieWithNothingKnown_WritesNullsAndEmptyLists()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { new Movie { Title = "Home Movies" } } });

        await CatalogFor(rewind).WriteAsync(ct);

        Assert.Equal(
            ["""{"title":"Home Movies","year":null,"rating":null,"minutes":null,"genres":[],"thumb":null,"upcs":[]}"""],
            MoviesIn(await ReadCatalogAsync(rewind, ct)));
    }

    // A UPC names an edition, and the catalog says "Already owned" for every edition we have.
    [Fact]
    public async Task WriteAsync_MovieInTwoEditions_ListsBothUpcs()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var jaws = Jaws();
        await AddInOrderAsync(
            rewind,
            ct,
            new Case { StoreLabel = "RV-00001", FactoryUpc = JawsUpc, Movies = { jaws } },
            new Case { StoreLabel = "RV-00002", FactoryUpc = JawsSecondEditionUpc, Movies = { jaws } });

        await CatalogFor(rewind).WriteAsync(ct);

        Assert.Equal([[JawsUpc, JawsSecondEditionUpc]], UpcsIn(await ReadCatalogAsync(rewind, ct)));
    }

    [Fact]
    public async Task WriteAsync_TwoCopiesOfOneEdition_ListsTheirUpcOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var jaws = Jaws();
        await AddInOrderAsync(
            rewind,
            ct,
            new Case { StoreLabel = "RV-00001", FactoryUpc = JawsUpc, Movies = { jaws } },
            new Case { StoreLabel = "RV-00002", FactoryUpc = JawsUpc, Movies = { jaws } });

        await CatalogFor(rewind).WriteAsync(ct);

        Assert.Equal([[JawsUpc]], UpcsIn(await ReadCatalogAsync(rewind, ct)));
    }

    // The movie is in a case that came with a barcode as well, so its list isn't empty for some other reason.
    [Fact]
    public async Task WriteAsync_CaseWithNoUpc_AddsNone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var jaws = Jaws();
        await AddInOrderAsync(
            rewind,
            ct,
            new Case { StoreLabel = "RV-00001", Movies = { jaws } },
            new Case { StoreLabel = "RV-00002", FactoryUpc = JawsUpc, Movies = { jaws } });

        await CatalogFor(rewind).WriteAsync(ct);

        Assert.Equal([[JawsUpc]], UpcsIn(await ReadCatalogAsync(rewind, ct)));
    }

    // A scan of the box set in a thrift store should say we own it, whichever of its movies the phone shows.
    [Fact]
    public async Task WriteAsync_BoxSet_GivesEachMovieTheSetsUpc()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(
            rewind,
            ct,
            new Case { StoreLabel = "RV-00001", FactoryUpc = BoxSetUpc, Movies = { Halloween(), new Movie { Title = "Halloween II", Year = 1981 } } });

        await CatalogFor(rewind).WriteAsync(ct);

        Assert.Equal([[BoxSetUpc], [BoxSetUpc]], UpcsIn(await ReadCatalogAsync(rewind, ct)));
    }

    // A movie whose only case is retired isn't ours any more, so it isn't listed, and its thumbnail isn't copied.
    [Fact]
    public async Task WriteAsync_MovieWhoseOnlyCaseIsRetired_LeavesItOut()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(
            rewind,
            ct,
            new Case { StoreLabel = "RV-00001", FactoryUpc = JawsUpc, Status = CaseStatus.Retired, Movies = { Jaws() } },
            new Case { StoreLabel = "RV-00002", Movies = { Halloween() } });
        await SaveThumbAsync(rewind, JawsId, JawsThumb, ct);

        var written = await CatalogFor(rewind).WriteAsync(ct);

        Assert.Equal(1, written);
        Assert.Equal([("Halloween", 1978, "R")], TitlesIn(await ReadCatalogAsync(rewind, ct)));
        Assert.Empty(OutboxFiles(rewind, "thumbs"));
    }

    [Fact]
    public async Task WriteAsync_MovieWithARetiredCaseAndOneOnTheShelf_ListsItWithoutTheRetiredUpc()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var jaws = Jaws();
        await AddInOrderAsync(
            rewind,
            ct,
            new Case { StoreLabel = "RV-00001", FactoryUpc = JawsUpc, Status = CaseStatus.Retired, Movies = { jaws } },
            new Case { StoreLabel = "RV-00002", FactoryUpc = JawsSecondEditionUpc, Movies = { jaws } });

        await CatalogFor(rewind).WriteAsync(ct);

        Assert.Equal([[JawsSecondEditionUpc]], UpcsIn(await ReadCatalogAsync(rewind, ct)));
    }

    // A case that's out tonight is still ours, and so is its barcode.
    [Fact]
    public async Task WriteAsync_MovieWhoseCaseIsCheckedOut_ListsItWithItsUpc()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(
            rewind,
            ct,
            new Case { StoreLabel = "RV-00001", FactoryUpc = JawsUpc, Status = CaseStatus.CheckedOut, Movies = { Jaws() } });

        await CatalogFor(rewind).WriteAsync(ct);

        Assert.Equal([[JawsUpc]], UpcsIn(await ReadCatalogAsync(rewind, ct)));
    }

    [Fact]
    public async Task WriteAsync_MovieInNoCase_LeavesItOut()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, Jaws(), new Case { StoreLabel = "RV-00001", Movies = { Halloween() } });

        var written = await CatalogFor(rewind).WriteAsync(ct);

        Assert.Equal(1, written);
        Assert.Equal([("Halloween", 1978, "R")], TitlesIn(await ReadCatalogAsync(rewind, ct)));
    }

    // The Ids run against every part of the order: the titles, the years, and the two movies that share a title and
    // a year, which go in Id order. Unknown years go first, and character codes put capitals before lower case, so
    // "home movies" comes after Jaws.
    [Fact]
    public async Task WriteAsync_Movies_AreInOrderOfTitleThenYearThenId()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var jaws = Jaws();
        var remake = new Movie { TmdbId = 424139, Title = "Halloween", Year = 2018, Rating = "R" };
        var original = Halloween();
        var originalByHand = new Movie { Title = "Halloween", Year = 1978 };
        var yearUnknown = new Movie { Title = "Halloween" };
        var homeMovies = new Movie { Title = "home movies" };
        await AddInOrderAsync(
            rewind,
            ct,
            jaws,
            remake,
            original,
            originalByHand,
            yearUnknown,
            homeMovies,
            new Case { StoreLabel = "RV-00001", Movies = { jaws, remake, original, originalByHand, yearUnknown, homeMovies } });

        await CatalogFor(rewind).WriteAsync(ct);

        Assert.Equal(
            [
                ("Halloween", null, null),
                ("Halloween", 1978, "R"),
                ("Halloween", 1978, null),
                ("Halloween", 2018, "R"),
                ("Jaws", 1975, "PG"),
                ("home movies", null, null),
            ],
            TitlesIn(await ReadCatalogAsync(rewind, ct)));
    }

    // Saved in the reverse of their names' order, so their Ids run against it.
    [Fact]
    public async Task WriteAsync_MovieWithSeveralGenres_ListsThemInOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var thriller = new Genre { TmdbId = 53, Name = "Thriller" };
        var horror = new Genre { TmdbId = 27, Name = "Horror" };
        var adventure = new Genre { TmdbId = 12, Name = "Adventure" };
        var jaws = Jaws();
        jaws.Genres.Add(horror);
        jaws.Genres.Add(thriller);
        jaws.Genres.Add(adventure);
        await AddInOrderAsync(rewind, ct, thriller, horror, adventure, new Case { StoreLabel = "RV-00001", Movies = { jaws } });

        await CatalogFor(rewind).WriteAsync(ct);

        var movie = Assert.Single((await ReadCatalogAsync(rewind, ct)).GetProperty("movies").EnumerateArray());
        Assert.Equal(["Adventure", "Horror", "Thriller"], movie.GetProperty("genres").EnumerateArray().Select(genre => genre.GetString()));
    }

    // The cases are saved in the reverse of their UPCs' order, so the case Ids run against it.
    [Fact]
    public async Task WriteAsync_MovieInSeveralEditions_ListsTheirUpcsInOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var jaws = Jaws();
        await AddInOrderAsync(
            rewind,
            ct,
            new Case { StoreLabel = "RV-00001", FactoryUpc = EuropeanUpc, Movies = { jaws } },
            new Case { StoreLabel = "RV-00002", FactoryUpc = JawsSecondEditionUpc, Movies = { jaws } },
            new Case { StoreLabel = "RV-00003", FactoryUpc = JawsUpc, Movies = { jaws } });

        await CatalogFor(rewind).WriteAsync(ct);

        Assert.Equal([[JawsUpc, JawsSecondEditionUpc, EuropeanUpc]], UpcsIn(await ReadCatalogAsync(rewind, ct)));
    }

    // TEST-2: the single query gives this movie nine rows, each of its three genres with each of its three UPCs,
    // and EF Core gathers them back into one movie. A repeat could only show in the genres, since the SortedSet would
    // hide one among the UPCs. Both are saved out of order, as in the tests above, so the order counts too.
    [Fact]
    public async Task WriteAsync_ThreeGenresInThreeEditions_ListsEachGenreAndUpcOnceInOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var thriller = new Genre { TmdbId = 53, Name = "Thriller" };
        var horror = new Genre { TmdbId = 27, Name = "Horror" };
        var adventure = new Genre { TmdbId = 12, Name = "Adventure" };
        var jaws = Jaws();
        jaws.Genres.Add(horror);
        jaws.Genres.Add(thriller);
        jaws.Genres.Add(adventure);
        await AddInOrderAsync(
            rewind,
            ct,
            thriller,
            horror,
            adventure,
            new Case { StoreLabel = "RV-00001", FactoryUpc = EuropeanUpc, Movies = { jaws } },
            new Case { StoreLabel = "RV-00002", FactoryUpc = JawsSecondEditionUpc, Movies = { jaws } },
            new Case { StoreLabel = "RV-00003", FactoryUpc = JawsUpc, Movies = { jaws } });

        await CatalogFor(rewind).WriteAsync(ct);

        Assert.Equal(
            ["""{"title":"Jaws","year":1975,"rating":"PG","minutes":124,"genres":["Adventure","Horror","Thriller"],"thumb":null,"upcs":["012345678905","085391163121","4006381333931"]}"""],
            MoviesIn(await ReadCatalogAsync(rewind, ct)));
    }

    // Named with a forward slash, since it's a web path from catalog.json.
    [Fact]
    public async Task WriteAsync_ThumbnailTheOutboxLacks_CopiesItAndNamesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { Jaws() } });
        await SaveThumbAsync(rewind, JawsId, JawsThumb, ct);

        await CatalogFor(rewind).WriteAsync(ct);

        Assert.Equal(JawsThumb, await File.ReadAllBytesAsync(InOutbox(rewind, "thumbs", "578.jpg"), ct));
        Assert.Equal(["thumbs/578.jpg"], ThumbsIn(await ReadCatalogAsync(rewind, ct)));
    }

    // Each Close Out would otherwise write every thumbnail again, and 7.2's upload would have nothing to go by.
    [Fact]
    public async Task WriteAsync_OutboxHasTheSameThumbnail_DoesntWriteItAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { Jaws() } });
        await SaveThumbAsync(rewind, JawsId, JawsThumb, ct);
        var copy = await PutInOutboxAsync(rewind, JawsId, JawsThumb, ct);
        File.SetLastWriteTimeUtc(copy, LongAgo);

        await CatalogFor(rewind).WriteAsync(ct);

        Assert.Equal(LongAgo, File.GetLastWriteTimeUtc(copy));
    }

    // Different bytes of the same length, so a check of the length alone would keep the old one.
    [Fact]
    public async Task WriteAsync_OutboxHasADifferentThumbnail_ReplacesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { Jaws() } });
        await SaveThumbAsync(rewind, JawsId, JawsThumb, ct);
        var copy = await PutInOutboxAsync(rewind, JawsId, OtherThumb, ct);

        await CatalogFor(rewind).WriteAsync(ct);

        Assert.Equal(JawsThumb, await File.ReadAllBytesAsync(copy, ct));
    }

    // Such as a movie whose poster never downloaded.
    [Fact]
    public async Task WriteAsync_NoThumbnailInTheDataFolder_NamesNone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { Jaws() } });

        await CatalogFor(rewind).WriteAsync(ct);

        Assert.Equal([null], ThumbsIn(await ReadCatalogAsync(rewind, ct)));
        Assert.Empty(OutboxFiles(rewind, "thumbs"));
    }

    // Such as after a restore, before the posters download again: the outbox still holds the one an earlier Close
    // Out copied, and catalog.json names what the outbox holds.
    [Fact]
    public async Task WriteAsync_ThumbnailOnlyInTheOutbox_StillNamesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { Jaws() } });
        var copy = await PutInOutboxAsync(rewind, JawsId, JawsThumb, ct);

        await CatalogFor(rewind).WriteAsync(ct);

        Assert.Equal(["thumbs/578.jpg"], ThumbsIn(await ReadCatalogAsync(rewind, ct)));
        Assert.Equal(JawsThumb, await File.ReadAllBytesAsync(copy, ct));
    }

    // Nothing is ever deleted from the outbox, and a thumbnail nothing names is harmless.
    [Fact]
    public async Task WriteAsync_ThumbnailTheCatalogNoLongerNames_StaysInTheOutbox()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Status = CaseStatus.Retired, Movies = { Jaws() } });
        await SaveThumbAsync(rewind, JawsId, JawsThumb, ct);
        await PutInOutboxAsync(rewind, JawsId, JawsThumb, ct);

        await CatalogFor(rewind).WriteAsync(ct);

        Assert.Equal(["catalog.json", "thumbs/578.jpg"], OutboxFiles(rewind));
    }

    // SEC-2: movies only. The family's names and card, the snack's name and code, the prices and the stars are all
    // saved, and none of them is written. Every field's name and value is pinned, so an eighth field, or a price in
    // a field that's there, fails this test: adding a field is a decision, and catalog-json.md says what it means.
    [Fact]
    public async Task WriteAsync_FamilyDataSaved_ListsMoviesOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddFamilyHistoryAsync(rewind, ct);

        await CatalogFor(rewind).WriteAsync(ct);

        var text = await File.ReadAllTextAsync(InOutbox(rewind, "catalog.json"), ct);
        var catalog = await ReadCatalogAsync(rewind, ct);
        Assert.Equal(["version", "generatedAt", "movies"], catalog.EnumerateObject().Select(field => field.Name));
        Assert.All(
            catalog.GetProperty("movies").EnumerateArray(),
            movie => Assert.Equal(["title", "year", "rating", "minutes", "genres", "thumb", "upcs"], movie.EnumerateObject().Select(field => field.Name)));
        Assert.Equal(
            [
                """{"title":"Halloween","year":1978,"rating":null,"minutes":null,"genres":[],"thumb":null,"upcs":[]}""",
                """{"title":"Halloween II","year":1981,"rating":null,"minutes":null,"genres":[],"thumb":null,"upcs":[]}""",
            ],
            MoviesIn(catalog));
        Assert.All(["Dad", "MEM-001", "Popcorn", "CON-POPCORN"], family => Assert.DoesNotContain(family, text, StringComparison.Ordinal));
    }

    [Fact]
    public async Task WriteAsync_SecondWrite_ReplacesTheFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { Jaws() } });
        await CatalogFor(rewind).WriteAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00002", Movies = { Halloween() } });
        rewind.Time.Advance(TimeSpan.FromDays(7));

        await CatalogFor(rewind).WriteAsync(ct);

        var catalog = await ReadCatalogAsync(rewind, ct);
        Assert.Equal("2026-10-31T02:15:00Z", catalog.GetProperty("generatedAt").GetString());
        Assert.Equal([("Halloween", 1978, "R"), ("Jaws", 1975, "PG")], TitlesIn(catalog));
        Assert.Equal(["catalog.json"], OutboxFiles(rewind)); // nothing left over from the write
    }

    // It fails at Scream's thumbnail, after Halloween's is copied: a folder stands where Scream's goes, so its new
    // file can't be moved into place, on Windows or on Linux. The copied thumbnail stays, since nothing in the
    // outbox is deleted, and catalog.json is still the one that doesn't name it.
    [Fact]
    public async Task WriteAsync_FailsPartway_LeavesTheEarlierCatalogWholeAndNoStrayFile()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { Jaws() } });
        await SaveThumbAsync(rewind, JawsId, JawsThumb, ct);
        await CatalogFor(rewind).WriteAsync(ct);
        var earlier = await File.ReadAllBytesAsync(InOutbox(rewind, "catalog.json"), ct);
        await AddInOrderAsync(
            rewind,
            ct,
            new Case { StoreLabel = "RV-00002", Movies = { Halloween() } },
            new Case { StoreLabel = "RV-00003", Movies = { new Movie { TmdbId = ScreamId, Title = "Scream", Year = 1996 } } });
        await SaveThumbAsync(rewind, HalloweenId, OtherThumb, ct);
        await SaveThumbAsync(rewind, ScreamId, OtherThumb, ct);
        Directory.CreateDirectory(InOutbox(rewind, "thumbs", "4232.jpg"));

        var failure = await Record.ExceptionAsync(() => CatalogFor(rewind).WriteAsync(ct));

        Assert.True(failure is IOException or UnauthorizedAccessException, $"Expected the move to fail, but got {failure}");
        Assert.Equal(earlier, await File.ReadAllBytesAsync(InOutbox(rewind, "catalog.json"), ct));
        Assert.Equal(["catalog.json", "thumbs/578.jpg", "thumbs/948.jpg"], OutboxFiles(rewind));
    }

    // NET-7: a call cancelled before it starts doesn't even make the outbox.
    [Fact]
    public async Task WriteAsync_CancelledBeforeItStarts_WritesNothing()
    {
        await using var rewind = new TempRewind();
        await rewind.StartAsync(TestContext.Current.CancellationToken);
        await AddInOrderAsync(rewind, TestContext.Current.CancellationToken, new Case { StoreLabel = "RV-00001", Movies = { Jaws() } });
        await SaveThumbAsync(rewind, JawsId, JawsThumb, TestContext.Current.CancellationToken);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CatalogFor(rewind).WriteAsync(cancelled.Token));

        Assert.False(Directory.Exists(InOutbox(rewind)));
    }

    // NET-7: cancelled once the read has every row, so the cancel lands on catalog.json's own write. Neither movie
    // has a thumbnail to copy, so nothing else hears it first.
    [Fact]
    public async Task WriteAsync_CancelledAfterTheRead_LeavesTheEarlierCatalogAsItWas()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { Jaws() } });
        await CatalogFor(rewind).WriteAsync(ct);
        var earlier = await File.ReadAllBytesAsync(InOutbox(rewind, "catalog.json"), ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00002", Movies = { new Movie { Title = "Home Movies" } } });
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CatalogFor(rewind, rewind.ContextsWith(new CancelAfterTheRead(cancel))).WriteAsync(cancel.Token));

        Assert.Equal(earlier, await File.ReadAllBytesAsync(InOutbox(rewind, "catalog.json"), ct));
        Assert.Equal(["catalog.json"], OutboxFiles(rewind));
    }

    // NET-7: a cancel once the read is done stops the thumbnails too, before the first one is copied.
    [Fact]
    public async Task WriteAsync_CancelledAfterTheRead_CopiesNoThumbnail()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { Jaws() } });
        await SaveThumbAsync(rewind, JawsId, JawsThumb, ct);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CatalogFor(rewind, rewind.ContextsWith(new CancelAfterTheRead(cancel))).WriteAsync(cancel.Token));

        Assert.Empty(OutboxFiles(rewind));
    }

    // The log line says the catalog went out, so a write that stops short doesn't log it.
    [Fact]
    public async Task WriteAsync_CancelledAfterTheRead_LogsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { Jaws() } });
        var log = new ListLogger<PhoneCatalog>();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CatalogFor(rewind, rewind.ContextsWith(new CancelAfterTheRead(cancel)), log).WriteAsync(cancel.Token));

        Assert.Empty(log.Entries);
    }

    // Information (OBS-3): a Close Out step, with how many movies the phones will know.
    [Fact]
    public async Task WriteAsync_Written_LogsHowManyMovies()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddInOrderAsync(rewind, ct, new Case { StoreLabel = "RV-00001", Movies = { Jaws(), Halloween() } });
        var log = new ListLogger<PhoneCatalog>();

        await CatalogFor(rewind, logger: log).WriteAsync(ct);

        Assert.Equal([(LogLevel.Information, "Wrote the phone catalog to the outbox: movies 2", (Exception?)null)], log.Entries);
    }

    // The register's log keeps Microsoft's warnings, and EF Core warns about a read of two lists per movie unless
    // it's told how to split it: a warning at every start's first Close Out, for nothing that's wrong. Here the
    // warning throws, in contexts that compile the read afresh (see SplitWarningThrows).
    [Fact]
    public async Task WriteAsync_ReadingTheCollection_TripsNoSplitWarning()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var failure = await Record.ExceptionAsync(() => CatalogFor(rewind, SplitWarningThrows(rewind)).WriteAsync(ct));

        Assert.Null(failure);
    }

    // The outbox has the NAS's catalog folder's shape from the first Close Out, so 7.2 can upload thumbs whatever
    // the collection holds.
    [Fact]
    public async Task WriteAsync_NoMovies_WritesAnEmptyCatalogBesideAThumbsFolder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var written = await CatalogFor(rewind).WriteAsync(ct);

        Assert.Equal((0, 0), (written, (await ReadCatalogAsync(rewind, ct)).GetProperty("movies").GetArrayLength()));
        Assert.True(Directory.Exists(InOutbox(rewind, "thumbs")));
    }

    private static PhoneCatalog CatalogFor(TempRewind rewind, IDbContextFactory<RewindDb>? dbFactory = null, ILogger<PhoneCatalog>? logger = null) =>
        new(dbFactory ?? rewind.DbFactory, Options.Create(rewind.Paths), rewind.Time, logger ?? NullLogger<PhoneCatalog>.Instance);

    // A new Jaws and Halloween for each test, so each database gets rows of its own.
    private static Movie Jaws() => new() { TmdbId = JawsId, Title = "Jaws", Year = 1975, Rating = "PG", RuntimeMinutes = 124 };

    private static Movie Halloween() => new() { TmdbId = HalloweenId, Title = "Halloween", Year = 1978, Rating = "R", RuntimeMinutes = 91 };

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

    // SaleParts' double feature, rented by Dad with a snack on the bill, back, and in the watch log with his stars.
    private static async Task AddFamilyHistoryAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, cancellationToken);
        var rental = parts.NewRental(totalCents: 474, cashTenderedCents: 500);
        var item = new RentalItem { CaseId = parts.CaseId, MovieId = parts.MovieId, PriceCents = 299, ReturnedAt = rental.RentedAt.AddHours(1) };
        rental.Items.Add(item);
        rental.Lines.Add(new SaleLine { Name = "Popcorn", PriceCents = 175, ConcessionItemId = parts.SnackId });
        db.Rentals.Add(rental);
        await db.SaveChangesAsync(cancellationToken);
        db.WatchLog.Add(new Viewing { RentalItemId = item.Id, MemberId = parts.MemberId, Stars = 4 });
        await db.SaveChangesAsync(cancellationToken);
    }

    // The register's own poster folder, data\thumbs.
    private static async Task SaveThumbAsync(TempRewind rewind, int tmdbId, byte[] jpeg, CancellationToken cancellationToken)
    {
        var thumbs = Path.Combine(rewind.Paths.Root, "data", "thumbs");
        Directory.CreateDirectory(thumbs);
        await File.WriteAllBytesAsync(Path.Combine(thumbs, $"{tmdbId}.jpg"), jpeg, cancellationToken);
    }

    // A thumbnail an earlier Close Out left in the outbox.
    private static async Task<string> PutInOutboxAsync(TempRewind rewind, int tmdbId, byte[] jpeg, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(InOutbox(rewind, "thumbs"));
        var copy = InOutbox(rewind, "thumbs", $"{tmdbId}.jpg");
        await File.WriteAllBytesAsync(copy, jpeg, cancellationToken);
        return copy;
    }

    private static string InOutbox(TempRewind rewind, params string[] path) => Path.Combine([rewind.Paths.Root, "outbox", .. path]);

    // Every file in that folder of the outbox and below, as a path from it with forward slashes, in order.
    private static List<string> OutboxFiles(TempRewind rewind, params string[] folder)
    {
        var outbox = InOutbox(rewind, folder);
        return [.. Directory.GetFiles(outbox, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(outbox, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];
    }

    private static async Task<JsonElement> ReadCatalogAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(InOutbox(rewind, "catalog.json"), cancellationToken));
        return document.RootElement.Clone();
    }

    // Each movie as JSON with no spaces, field for field as the file has them.
    private static List<string> MoviesIn(JsonElement catalog) =>
        [.. catalog.GetProperty("movies").EnumerateArray().Select(movie => JsonSerializer.Serialize(movie))];

    private static List<(string? Title, int? Year, string? Rating)> TitlesIn(JsonElement catalog) =>
        [.. catalog.GetProperty("movies").EnumerateArray().Select(movie => (
            movie.GetProperty("title").GetString(),
            movie.GetProperty("year").ValueKind == JsonValueKind.Null ? (int?)null : movie.GetProperty("year").GetInt32(),
            movie.GetProperty("rating").GetString()))];

    // Each movie's UPCs, as the file has them, nulls and all.
    private static List<List<string?>> UpcsIn(JsonElement catalog) =>
        [.. catalog.GetProperty("movies").EnumerateArray().Select(movie => movie.GetProperty("upcs").EnumerateArray().Select(upc => upc.GetString()).ToList())];

    private static List<string?> ThumbsIn(JsonElement catalog) =>
        [.. catalog.GetProperty("movies").EnumerateArray().Select(movie => movie.GetProperty("thumb").GetString())];

    // Contexts in which EF Core's warning about a query with two lists it wasn't told how to split throws, rather
    // than going to a log. EF Core compiles a query once for every context that shares its internal services, and
    // warns only then, so a read that another test compiled first would never warn in a test's own log. A warning set
    // to throw is part of what EF Core builds those services for, so these contexts get services of their own, and the
    // read compiles here, whichever test ran first.
    private static IDbContextFactory<RewindDb> SplitWarningThrows(TempRewind rewind) =>
        rewind.ContextsWith(warnings: warnings => warnings.Throw(RelationalEventId.MultipleCollectionIncludeWarning));
}
