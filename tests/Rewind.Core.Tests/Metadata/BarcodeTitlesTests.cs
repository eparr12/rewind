using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rewind.Core.Data;
using Rewind.Core.Metadata;

namespace Rewind.Core.Tests.Metadata;

// Each test gets its own Rewind folder, clock and pacer (TEST-7), over real SQLite (TEST-3). The
// pacer's own rules are in UpcItemDbPacerTests.
public sealed class BarcodeTitlesTests : IAsyncLifetime
{
    private readonly TempRewind _rewind = new();
    private readonly FakeUpcItemDb _upcItemDb = new();
    private readonly HttpClient _http;
    private readonly UpcItemDbPacer _pacer;
    private BarcodeTitles? _titles;

    public BarcodeTitlesTests()
    {
        _http = new HttpClient(_upcItemDb);
        _pacer = new UpcItemDbPacer(_rewind.Time, NullLogger<UpcItemDbPacer>.Instance);
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.TommyBoy);
        _upcItemDb.AnswerLookup(UpcJson.HalloweenUpc, UpcJson.Items("Halloween (Widescreen DVD)"));
    }

    // Made once the database is ready, and once only: UpcItemDbClient sets up the HttpClient it's given.
    private BarcodeTitles Titles => _titles ??= new BarcodeTitles(Client(), _pacer, _rewind.DbFactory, _rewind.Time, NullLogger<BarcodeTitles>.Instance);

    public async ValueTask InitializeAsync() => await _rewind.StartAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        _pacer.Dispose();
        _http.Dispose();
        await _rewind.DisposeAsync();
    }

    // Build Plan 2.2's check, first part: a DVD's UPC gives a clean title.
    [Fact]
    public async Task LookupAsync_NewUpc_FindsTheCleanTitle()
    {
        var title = await LookUpAsync(UpcJson.TommyBoyUpc);

        Assert.Equal(new BarcodeTitle(UpcJson.TommyBoyUpc, UpcStatus.Found, "Tommy Boy (DVD)", "Tommy Boy", null, null), title);
    }

    [Fact]
    public async Task LookupAsync_NewUpc_AsksOnceAndSavesOneRow()
    {
        var ct = TestContext.Current.CancellationToken;

        await LookUpAsync(UpcJson.TommyBoyUpc);

        Assert.Single(_upcItemDb.Requests);
        await using var db = _rewind.OpenDb();
        var saved = await db.UpcLookups.SingleAsync(ct);
        Assert.Equal((UpcJson.TommyBoyUpc, "Tommy Boy (DVD)"), (saved.Upc, saved.ProductTitle));
    }

    // Build Plan 2.2's check, second part: scanning it again makes no web request.
    [Fact]
    public async Task LookupAsync_SameUpcAgain_AsksNothing()
    {
        await LookUpAsync(UpcJson.TommyBoyUpc);

        var again = await LookUpAsync(UpcJson.TommyBoyUpc);

        Assert.Single(_upcItemDb.Requests);
        Assert.Equal("Tommy Boy", again.Title);
    }

    // NET-8, DATA-8: when UPCitemdb answered, from the injected clock, in UTC.
    [Fact]
    public async Task LookupAsync_NewUpc_SavesWhenFromTheClock()
    {
        var ct = TestContext.Current.CancellationToken;

        await LookUpAsync(UpcJson.TommyBoyUpc);

        await using var db = _rewind.OpenDb();
        Assert.Equal(_rewind.Time.GetUtcNow().UtcDateTime, await db.UpcLookups.Select(u => u.LookedUpAt).SingleAsync(ct));
    }

    // Not found is an answer too, saved with a NULL title (DATA-3).
    [Fact]
    public async Task LookupAsync_UpcItemDbDoesNotKnowIt_SavesNoTitle()
    {
        var ct = TestContext.Current.CancellationToken;
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.NoItems);

        var title = await LookUpAsync(UpcJson.TommyBoyUpc);

        Assert.Equal(UpcStatus.NotFound, title.Status);
        await using var db = _rewind.OpenDb();
        Assert.Null(await db.UpcLookups.Where(u => u.Upc == UpcJson.TommyBoyUpc).Select(u => u.ProductTitle).SingleAsync(ct));
    }

    // Saved for a month, to the second.
    [Fact]
    public async Task LookupAsync_NotFoundWithinAMonth_AsksNothing()
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.NoItems);
        await LookUpAsync(UpcJson.TommyBoyUpc);
        _rewind.Time.Advance(BarcodeTitles.NotFoundKeptFor - TimeSpan.FromSeconds(1));

        var again = await LookUpAsync(UpcJson.TommyBoyUpc);

        Assert.Single(_upcItemDb.Requests);
        Assert.Equal(new BarcodeTitle(UpcJson.TommyBoyUpc, UpcStatus.NotFound, null, null, null, null), again);
    }

    // UPCitemdb keeps growing, and a not found can come from a bad hour, so a month on, the barcode
    // is asked about again, and the new answer takes the old one's row.
    [Fact]
    public async Task LookupAsync_NotFoundAMonthAgo_AsksAgainAndKeepsTheNewAnswer()
    {
        var ct = TestContext.Current.CancellationToken;
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.NoItems);
        await LookUpAsync(UpcJson.TommyBoyUpc);
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.TommyBoy);
        _rewind.Time.Advance(BarcodeTitles.NotFoundKeptFor);

        var again = await LookUpAsync(UpcJson.TommyBoyUpc);

        Assert.Equal(2, _upcItemDb.Requests.Count);
        Assert.Equal("Tommy Boy", again.Title);
        await using var db = _rewind.OpenDb();
        var saved = await db.UpcLookups.SingleAsync(ct);
        Assert.Equal(("Tommy Boy (DVD)", _rewind.Time.GetUtcNow().UtcDateTime), (saved.ProductTitle, saved.LookedUpAt));
    }

    // A found answer never goes stale: the product's title doesn't change.
    [Fact]
    public async Task LookupAsync_FoundAYearAgo_AsksNothing()
    {
        await LookUpAsync(UpcJson.TommyBoyUpc);
        _rewind.Time.Advance(TimeSpan.FromDays(365));

        await LookUpAsync(UpcJson.TommyBoyUpc);

        Assert.Single(_upcItemDb.Requests);
    }

    // A product's title that cleans to nothing is saved as UPCitemdb gave it, but gives no title.
    [Fact]
    public async Task LookupAsync_TitleIsOnlyEditionWords_IsNotFound()
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.Items("Widescreen DVD"));

        var title = await LookUpAsync(UpcJson.TommyBoyUpc);

        Assert.Equal(new BarcodeTitle(UpcJson.TommyBoyUpc, UpcStatus.NotFound, "Widescreen DVD", null, null, null), title);
    }

    // Build Plan 2.2: when UPCitemdb can't answer, the clerk types the title, and nothing is saved.
    [Fact]
    public async Task LookupAsync_UpcItemDbCannotAnswer_SavesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.Error("SERVER_ERR", "Server error"), HttpStatusCode.InternalServerError);

        var title = await LookUpAsync(UpcJson.TommyBoyUpc);

        Assert.Equal(new BarcodeTitle(UpcJson.TommyBoyUpc, UpcStatus.TryLater, null, null, UpcTryLaterReason.ServerError, null), title);
        await using var db = _rewind.OpenDb();
        Assert.Equal(0, await db.UpcLookups.CountAsync(ct));
    }

    [Fact]
    public async Task LookupAsync_AfterUpcItemDbCouldNotAnswer_AsksAgain()
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.Error("SERVER_ERR", "Server error"), HttpStatusCode.InternalServerError);
        await LookUpAsync(UpcJson.TommyBoyUpc);
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.TommyBoy);

        var again = await LookUpAsync(UpcJson.TommyBoyUpc);

        Assert.Equal(2, _upcItemDb.Requests.Count);
        Assert.Equal("Tommy Boy", again.Title);
    }

    // For the screen to say "out of lookups until 7:00 PM": when Retry-After says, from the clock.
    [Fact]
    public async Task LookupAsync_OutOfLookups_SaysWhenTheyComeBack()
    {
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.ExceedLimit, HttpStatusCode.TooManyRequests, remaining: 0, retryAfter: "7200");

        var title = await LookUpAsync(UpcJson.TommyBoyUpc);

        Assert.Equal(UpcTryLaterReason.DailyLimit, title.Reason);
        Assert.Equal(_rewind.Time.GetUtcNow() + TimeSpan.FromHours(2), title.RetryAt);
    }

    // The EAN and its UPC-A are one product, kept under the 12-digit form, as CK_UpcLookups_Upc
    // and the phone catalog expect.
    [Fact]
    public async Task LookupAsync_EanThenItsUpcA_ShareOneRow()
    {
        var ct = TestContext.Current.CancellationToken;
        await LookUpAsync(UpcJson.TommyBoyEan);

        var upcA = await LookUpAsync(UpcJson.TommyBoyUpc);

        Assert.Single(_upcItemDb.Requests);
        Assert.Equal(UpcJson.TommyBoyUpc, upcA.Upc);
        await using var db = _rewind.OpenDb();
        Assert.Equal(UpcJson.TommyBoyUpc, await db.UpcLookups.Select(u => u.Upc).SingleAsync(ct));
    }

    // SEC-5: a code that isn't a factory barcode costs no lookup and leaves no row.
    [Theory]
    [InlineData("032429258374")] // the check digit is wrong
    [InlineData("RV-00042")]
    public async Task LookupAsync_NotAFactoryBarcode_AsksNothingAndSavesNothing(string code)
    {
        var ct = TestContext.Current.CancellationToken;

        var title = await LookUpAsync(code);

        Assert.Equal(new BarcodeTitle(null, UpcStatus.NotFound, null, null, null, null), title);
        Assert.Empty(_upcItemDb.Requests);
        await using var db = _rewind.OpenDb();
        Assert.Equal(0, await db.UpcLookups.CountAsync(ct));
    }

    // Two scans of one barcode at once both miss the cache, so the second asks too, once it's its
    // turn. They agree on the one row the first saved.
    [Fact]
    public async Task LookupAsync_TwoLookupsOfOneUpcAtOnce_AgreeOnOneRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var answer = _upcItemDb.HoldLookup(UpcJson.TommyBoyUpc, UpcJson.TommyBoy);
        var first = LookUpAsync(UpcJson.TommyBoyUpc);
        var second = LookUpAsync(UpcJson.TommyBoyUpc);

        answer.SetResult();

        Assert.Equal(await first, await second);
        Assert.Equal(2, _upcItemDb.Requests.Count);
        await using var db = _rewind.OpenDb();
        Assert.Equal(1, await db.UpcLookups.CountAsync(ct));
    }

    // Another save of the barcode lands between this one's look at UpcLookups and its new row, so
    // UpcLookups.Upc's unique index refuses the row, and the answer saved first stands. A save
    // interceptor makes that other save, as no lookup through the screen could today.
    [Fact]
    public async Task LookupAsync_AnotherSaveWinsTheUniqueIndex_KeepsItsAnswer()
    {
        var ct = TestContext.Current.CancellationToken;
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.NoItems);
        _titles = new BarcodeTitles(Client(), _pacer, new BeatenToEachSave(_rewind), _rewind.Time, NullLogger<BarcodeTitles>.Instance);

        var title = await LookUpAsync(UpcJson.TommyBoyUpc);

        Assert.Equal("Tommy Boy", title.Title);
        await using var db = _rewind.OpenDb();
        Assert.Equal("Tommy Boy (DVD)", await db.UpcLookups.Select(u => u.ProductTitle).SingleAsync(ct));
    }

    // While another lookup's answer was on its way, the same barcode was found and saved, so this
    // answer, a not found a month later, doesn't overwrite it.
    [Fact]
    public async Task LookupAsync_TitleSavedMeanwhile_IsNotOverwritten()
    {
        var ct = TestContext.Current.CancellationToken;
        _upcItemDb.AnswerLookup(UpcJson.TommyBoyUpc, UpcJson.NoItems);
        await LookUpAsync(UpcJson.TommyBoyUpc);
        _rewind.Time.Advance(BarcodeTitles.NotFoundKeptFor);
        _upcItemDb.AnswerLater(FakeUpcItemDb.LookupFor(UpcJson.TommyBoyUpc), async () =>
        {
            await using var db = _rewind.OpenDb();
            var row = await db.UpcLookups.SingleAsync(ct);
            row.ProductTitle = "Tommy Boy (DVD)";
            await db.SaveChangesAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(UpcJson.NoItems, Encoding.UTF8, "application/json") };
        });

        var title = await LookUpAsync(UpcJson.TommyBoyUpc);

        Assert.Equal("Tommy Boy", title.Title);
        await using var fresh = _rewind.OpenDb();
        Assert.Equal("Tommy Boy (DVD)", await fresh.UpcLookups.Select(u => u.ProductTitle).SingleAsync(ct));
    }

    private Task<BarcodeTitle> LookUpAsync(string code) => Titles.LookupAsync(code, null, TestContext.Current.CancellationToken);

    private UpcItemDbClient Client() => new(_http, Options.Create(new UpcItemDbOptions()), NullLogger<UpcItemDbClient>.Instance);

    // Contexts each of whose saves another save beats: just before it, a row for Tommy Boy is saved
    // through a context of its own.
    private sealed class BeatenToEachSave(TempRewind rewind) : IDbContextFactory<RewindDb>
    {
        public RewindDb CreateDbContext() =>
            new(new DbContextOptionsBuilder<RewindDb>()
                .UseSqlite(RewindDb.ConnectionStringFor(rewind.Paths.Database))
                .AddInterceptors(new SaveFirst(rewind))
                .Options);
    }

    private sealed class SaveFirst(TempRewind rewind) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await using var other = rewind.OpenDb();
            other.UpcLookups.Add(new UpcLookup { Upc = UpcJson.TommyBoyUpc, ProductTitle = "Tommy Boy (DVD)", LookedUpAt = rewind.Time.GetUtcNow().UtcDateTime });
            await other.SaveChangesAsync(cancellationToken);
            return result;
        }
    }
}
