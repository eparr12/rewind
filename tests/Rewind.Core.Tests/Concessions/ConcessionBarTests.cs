using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Rewind.Core.Concessions;
using Rewind.Core.Data;

namespace Rewind.Core.Tests.Concessions;

public sealed class ConcessionBarTests
{
    private const string Code = "CON-POPCORN";

    [Fact]
    public async Task AddAsync_NameCodeAndPrice_SavesAnActiveItem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var change = await Bar(rewind).AddAsync("Popcorn", Code, "1.00", ct);

        var saved = Assert.Single(await SavedItemsAsync(rewind, ct));
        Assert.Equal(new ConcessionChange(ConcessionChangeStatus.Saved, saved), change);
        Assert.Equal(("Popcorn", Code, 100, true), (saved.Name, saved.Code, saved.PriceCents, saved.IsActive));
    }

    // Read as a scan is, so a code typed in lower case is saved as the menu's barcode will carry it.
    [Fact]
    public async Task AddAsync_CodeInLowerCase_SavesItInCapitals()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        await Bar(rewind).AddAsync("Popcorn", " con-popcorn ", "1.00", ct);

        Assert.Equal(Code, Assert.Single(await SavedItemsAsync(rewind, ct)).Code);
    }

    [Fact]
    public async Task AddAsync_NameLongerThanTheLimit_SavesItCut()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        await Bar(rewind).AddAsync(new string('A', ConcessionItem.MaxNameLength + 5), Code, "1.00", ct);

        Assert.Equal(new string('A', ConcessionItem.MaxNameLength), Assert.Single(await SavedItemsAsync(rewind, ct)).Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddAsync_BlankName_SavesNothing(string name)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var change = await Bar(rewind).AddAsync(name, Code, "1.00", ct);

        Assert.Equal(new ConcessionChange(ConcessionChangeStatus.NoName, null), change);
        Assert.Empty(await SavedItemsAsync(rewind, ct));
    }

    [Theory]
    [InlineData("POPCORN")]
    [InlineData("CON-")]
    [InlineData("CON-POP2")]
    [InlineData("CON-ABCDEFGHIJKLMNOPQRSTU")] // 21 letters
    [InlineData("MEM-001")]
    public async Task AddAsync_NotAConcessionCode_SavesNothing(string code)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var change = await Bar(rewind).AddAsync("Popcorn", code, "1.00", ct);

        Assert.Equal(new ConcessionChange(ConcessionChangeStatus.NotACode, null), change);
        Assert.Empty(await SavedItemsAsync(rewind, ct));
    }

    // 100 is $100.00: past the limit every price in the store has.
    [Theory]
    [InlineData("one")]
    [InlineData("")]
    [InlineData("100")]
    public async Task AddAsync_NotAPrice_SavesNothing(string price)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var change = await Bar(rewind).AddAsync("Popcorn", Code, price, ct);

        Assert.Equal(new ConcessionChange(ConcessionChangeStatus.NotAPrice, null), change);
        Assert.Empty(await SavedItemsAsync(rewind, ct));
    }

    // In the order the Store Manager fills them in: the name, then the code, then the price.
    [Theory]
    [InlineData("", "POPCORN", "one", ConcessionChangeStatus.NoName)]
    [InlineData("Popcorn", "POPCORN", "one", ConcessionChangeStatus.NotACode)]
    public async Task AddAsync_SeveralThingsWrong_SaysTheFirst(string name, string code, string price, ConcessionChangeStatus status)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var change = await Bar(rewind).AddAsync(name, code, price, ct);

        Assert.Equal(status, change.Status);
    }

    // DATA-11: a code belongs to one item for good, active or not, since a printed menu may still carry it.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddAsync_CodeAnotherItemHas_IsCodeInUse(bool ownerIsActive)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var owner = await AddItemAsync(rewind, "Popcorn", Code, 100, ct, ownerIsActive);

        var change = await Bar(rewind).AddAsync("Kettle corn", Code, "1.25", ct);

        Assert.Equal(new ConcessionChange(ConcessionChangeStatus.CodeInUse, owner), change);
        Assert.Equal([owner], await SavedItemsAsync(rewind, ct));
    }

    // The code was free when the bar looked, and another item took it before the save.
    [Fact]
    public async Task AddAsync_CodeTakenDuringTheSave_IsCodeInUse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var change = await Bar(rewind, new CodeTakenJustBeforeEachSave(rewind)).AddAsync("Kettle corn", Code, "1.25", ct);

        Assert.Equal(ConcessionChangeStatus.CodeInUse, change.Status);
        Assert.Equal("Popcorn", change.Item?.Name);
    }

    // Left to the unique index, EF Core would log the refused save as an error.
    [Fact]
    public async Task AddAsync_CodeInUse_LogsNoError()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddItemAsync(rewind, "Popcorn", Code, 100, ct);
        var log = new ListLoggerProvider();
        using var logged = new LoggedContexts(rewind, log);

        await Bar(rewind, logged).AddAsync("Kettle corn", Code, "1.25", ct);

        Assert.Contains(log.Entries, entry => entry.Category.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)); // the log is EF Core's
        Assert.DoesNotContain(log.Entries, entry => entry.Level >= LogLevel.Error);
    }

    // OBS-3: a code turning up twice is worth finding in the log later.
    [Fact]
    public async Task AddAsync_CodeInUse_LogsTheCode()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddItemAsync(rewind, "Popcorn", Code, 100, ct);
        var log = new ListLogger<ConcessionBar>();

        await new ConcessionBar(rewind.DbFactory, log).AddAsync("Kettle corn", Code, "1.25", ct);

        Assert.Equal([(LogLevel.Information, "Snack code CON-POPCORN is Popcorn's already, so nothing was saved", (Exception?)null)], log.Entries);
    }

    // NOCASE, so a name typed in lower case sorts among the others, not after every capital.
    [Fact]
    public async Task ListAsync_Items_InOrderOfName()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddItemAsync(rewind, "Popcorn", Code, 100, ct);
        await AddItemAsync(rewind, "candy", "CON-CANDY", 50, ct);
        await AddItemAsync(rewind, "Fountain pop", "CON-POP", 75, ct, isActive: false);

        var items = await Bar(rewind).ListAsync(ct);

        Assert.Equal(["candy", "Fountain pop", "Popcorn"], items.Select(item => item.Name));
    }

    // Two snacks can share a name, such as a soda in a can and one in a bottle, so the list has a second
    // order: the code.
    [Fact]
    public async Task ListAsync_TwoItemsWithOneName_ListsThemByCode()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddItemAsync(rewind, "Soda", "CON-CANSODA", 75, ct);
        await AddItemAsync(rewind, "Soda", "CON-BOTTLESODA", 100, ct);

        var items = await Bar(rewind).ListAsync(ct);

        Assert.Equal(["CON-BOTTLESODA", "CON-CANSODA"], items.Select(item => item.Code));
    }

    [Fact]
    public async Task FindByCodeAsync_CodeOfAnItem_FindsIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var popcorn = await AddItemAsync(rewind, "Popcorn", Code, 100, ct);
        await AddItemAsync(rewind, "Candy", "CON-CANDY", 50, ct);

        var item = await Bar(rewind).FindByCodeAsync(Code, ct);

        Assert.Equal(popcorn, item);
    }

    // A deactivated item keeps its code (DATA-4), and a menu printed before may still carry it.
    [Fact]
    public async Task FindByCodeAsync_CodeOfAnInactiveItem_FindsIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var candy = await AddItemAsync(rewind, "Candy", "CON-CANDY", 50, ct, isActive: false);

        var item = await Bar(rewind).FindByCodeAsync("CON-CANDY", ct);

        Assert.Equal(candy, item);
    }

    [Fact]
    public async Task FindByCodeAsync_CodeNoItemHas_FindsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddItemAsync(rewind, "Popcorn", Code, 100, ct);

        var item = await Bar(rewind).FindByCodeAsync("CON-NACHOS", ct);

        Assert.Null(item);
    }

    // Barcodes reads a scan in any case, as the scanner's settings may send it.
    [Fact]
    public async Task FindByCodeAsync_CodeInLowerCase_FindsTheItem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var popcorn = await AddItemAsync(rewind, "Popcorn", Code, 100, ct);

        var item = await Bar(rewind).FindByCodeAsync("con-popcorn", ct);

        Assert.Equal(popcorn, item);
    }

    // Only a concession code can be an item's, so nothing else needs the database.
    [Theory]
    [InlineData("RV-00042")] // a store label
    [InlineData("036000291452")] // a factory UPC
    [InlineData("MEM-001")] // a member card
    [InlineData("CON-POP2")] // a digit in it
    public async Task FindByCodeAsync_NotAConcessionCode_FindsNothing(string barcode)
    {
        var item = await WithNoDatabase().FindByCodeAsync(barcode, TestContext.Current.CancellationToken);

        Assert.Null(item);
    }

    // NET-7: the lookup hands its token on to the database, so a status bar that closes stops it.
    [Fact]
    public async Task FindByCodeAsync_Cancelled_Stops()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddItemAsync(rewind, "Popcorn", Code, 100, ct);
        using var closing = new CancellationTokenSource();
        await closing.CancelAsync();

        var find = () => Bar(rewind).FindByCodeAsync(Code, closing.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(find);
    }

    [Fact]
    public async Task FindByCodeAsync_Null_IsRefused()
    {
        // NET-1: null on purpose, past the compiler's check, since this tests what FindByCodeAsync does with one.
        var find = () => WithNoDatabase().FindByCodeAsync(null!, TestContext.Current.CancellationToken);

        var refusal = await Assert.ThrowsAsync<ArgumentNullException>(find);
        Assert.Equal("code", refusal.ParamName);
    }

    // Renaming is for typos, so the code and the price stay.
    [Fact]
    public async Task RenameAsync_NewName_KeepsTheCodeAndPrice()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var item = await AddItemAsync(rewind, "Popcron", Code, 100, ct);

        var change = await Bar(rewind).RenameAsync(item.Id, "Popcorn", ct);

        var renamed = item with { Name = "Popcorn" };
        Assert.Equal(new ConcessionChange(ConcessionChangeStatus.Saved, renamed), change);
        Assert.Equal([renamed], await SavedItemsAsync(rewind, ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RenameAsync_BlankName_SavesNothing(string name)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var item = await AddItemAsync(rewind, "Popcorn", Code, 100, ct);

        var change = await Bar(rewind).RenameAsync(item.Id, name, ct);

        Assert.Equal(new ConcessionChange(ConcessionChangeStatus.NoName, null), change);
        Assert.Equal([item], await SavedItemsAsync(rewind, ct));
    }

    // Items are never deleted (DATA-4), so an Id the Store Manager's list doesn't have is a bug.
    // SetPriceAsync and SetActiveAsync find the item the same way, through FindAsync, so this one test
    // covers all three.
    [Fact]
    public async Task RenameAsync_NoSuchItem_IsABug()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var rename = () => Bar(rewind).RenameAsync(42, "Popcorn", ct);

        await Assert.ThrowsAsync<InvalidOperationException>(rename);
    }

    [Fact]
    public async Task SetPriceAsync_NewPrice_SavesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var item = await AddItemAsync(rewind, "Popcorn", Code, 100, ct);

        var change = await Bar(rewind).SetPriceAsync(item.Id, "1.25", ct);

        var repriced = item with { PriceCents = 125 };
        Assert.Equal(new ConcessionChange(ConcessionChangeStatus.Saved, repriced), change);
        Assert.Equal([repriced], await SavedItemsAsync(rewind, ct));
    }

    [Theory]
    [InlineData("one")]
    [InlineData("100")]
    public async Task SetPriceAsync_NotAPrice_SavesNothing(string price)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var item = await AddItemAsync(rewind, "Popcorn", Code, 100, ct);

        var change = await Bar(rewind).SetPriceAsync(item.Id, price, ct);

        Assert.Equal(new ConcessionChange(ConcessionChangeStatus.NotAPrice, null), change);
        Assert.Equal([item], await SavedItemsAsync(rewind, ct));
    }

    // DATA-4: deactivating keeps the item, with its code.
    [Fact]
    public async Task SetActiveAsync_False_KeepsTheItemInactive()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var item = await AddItemAsync(rewind, "Popcorn", Code, 100, ct);

        var change = await Bar(rewind).SetActiveAsync(item.Id, isActive: false, ct);

        var inactive = item with { IsActive = false };
        Assert.Equal(new ConcessionChange(ConcessionChangeStatus.Saved, inactive), change);
        Assert.Equal([inactive], await SavedItemsAsync(rewind, ct));
    }

    // OBS-3: the database keeps each item only as it is now, so the log is where a change shows, with
    // prices in dollars for the family reading it.
    [Fact]
    public async Task EachChange_AnyItem_IsLogged()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var log = new ListLogger<ConcessionBar>();
        var bar = new ConcessionBar(rewind.DbFactory, log);

        var added = await bar.AddAsync("Popcron", Code, "1.00", ct);
        await bar.RenameAsync(added.Item!.Id, "Popcorn", ct);
        await bar.SetPriceAsync(added.Item.Id, "1.25", ct);
        await bar.SetActiveAsync(added.Item.Id, isActive: false, ct);
        await bar.SetActiveAsync(added.Item.Id, isActive: true, ct);

        Assert.Equal(
            [
                (LogLevel.Information, "Added snack CON-POPCORN, Popcron, at $1.00", (Exception?)null),
                (LogLevel.Information, "Renamed snack CON-POPCORN from Popcron to Popcorn", null),
                (LogLevel.Information, "Snack CON-POPCORN, Popcorn, changed from $1.00 to $1.25", null),
                (LogLevel.Information, "Snack CON-POPCORN, Popcorn, is inactive", null),
                (LogLevel.Information, "Snack CON-POPCORN, Popcorn, is active", null),
            ],
            log.Entries);
    }

    // A change to what's saved already is no change, so the log stays quiet: a name the same once cleaned,
    // a price typed another way, and the state the item is in.
    [Fact]
    public async Task EachChange_SameAsSaved_LogsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var item = await AddItemAsync(rewind, "Popcorn", Code, 100, ct);
        var log = new ListLogger<ConcessionBar>();
        var bar = new ConcessionBar(rewind.DbFactory, log);

        await bar.RenameAsync(item.Id, " Popcorn ", ct);
        await bar.SetPriceAsync(item.Id, "1", ct);
        await bar.SetActiveAsync(item.Id, isActive: true, ct);

        Assert.Empty(log.Entries);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("code")]
    [InlineData("price")]
    public async Task AddAsync_Null_IsRefusedByName(string parameter)
    {
        // NET-1: null on purpose, past the compiler's check, since this tests what AddAsync does with one.
        var add = () => WithNoDatabase().AddAsync(
            parameter == "name" ? null! : "Popcorn",
            parameter == "code" ? null! : Code,
            parameter == "price" ? null! : "1.00",
            TestContext.Current.CancellationToken);

        var refusal = await Assert.ThrowsAsync<ArgumentNullException>(add);
        Assert.Equal(parameter, refusal.ParamName);
    }

    [Fact]
    public async Task RenameAsync_NullName_IsRefused()
    {
        // NET-1: as above.
        var rename = () => WithNoDatabase().RenameAsync(1, null!, TestContext.Current.CancellationToken);

        var refusal = await Assert.ThrowsAsync<ArgumentNullException>(rename);
        Assert.Equal("name", refusal.ParamName);
    }

    [Fact]
    public async Task SetPriceAsync_NullPrice_IsRefused()
    {
        // NET-1: as above.
        var reprice = () => WithNoDatabase().SetPriceAsync(1, null!, TestContext.Current.CancellationToken);

        var refusal = await Assert.ThrowsAsync<ArgumentNullException>(reprice);
        Assert.Equal("price", refusal.ParamName);
    }

    private static ConcessionBar Bar(TempRewind rewind, IDbContextFactory<RewindDb>? dbFactory = null) =>
        new(dbFactory ?? rewind.DbFactory, NullLogger<ConcessionBar>.Instance);

    // For what's answered before the database is opened, such as a null.
    private static ConcessionBar WithNoDatabase() => new(new NoDatabase(), NullLogger<ConcessionBar>.Instance);

    // An item added before the test, saved straight to the database.
    private static async Task<ConcessionRow> AddItemAsync(
        TempRewind rewind, string name, string code, int priceCents, CancellationToken cancellationToken, bool isActive = true)
    {
        await using var db = rewind.OpenDb();
        var item = new ConcessionItem { Name = name, Code = code, PriceCents = priceCents, IsActive = isActive };
        db.ConcessionItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return new ConcessionRow(item.Id, name, code, priceCents, isActive);
    }

    // The items as the database holds them, in the order they were added.
    private static async Task<List<ConcessionRow>> SavedItemsAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        return await db.ConcessionItems
            .AsNoTracking()
            .OrderBy(i => i.Id)
            .Select(i => new ConcessionRow(i.Id, i.Name, i.Code, i.PriceCents, i.IsActive))
            .ToListAsync(cancellationToken);
    }

    private sealed class NoDatabase : IDbContextFactory<RewindDb>
    {
        public RewindDb CreateDbContext() => throw new InvalidOperationException("The concession bar opened the database.");
    }

    // Contexts each of whose saves another item beats to the code: just before the save, Popcorn is saved
    // with CON-POPCORN through a context of its own.
    private sealed class CodeTakenJustBeforeEachSave(TempRewind rewind) : IDbContextFactory<RewindDb>
    {
        public RewindDb CreateDbContext() =>
            new(new DbContextOptionsBuilder<RewindDb>()
                .UseSqlite(RewindDb.ConnectionStringFor(rewind.Paths.Database))
                .AddInterceptors(new TakeTheCodeFirst(rewind))
                .Options);
    }

    private sealed class TakeTheCodeFirst(TempRewind rewind) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await AddItemAsync(rewind, "Popcorn", Code, 100, cancellationToken);
            return result;
        }
    }
}
