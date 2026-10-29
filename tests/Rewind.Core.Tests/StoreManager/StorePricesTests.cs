using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Rewind.Core.Data;
using Rewind.Core.Labels;
using Rewind.Core.StoreManager;

namespace Rewind.Core.Tests.StoreManager;

public sealed class StorePricesTests
{
    // The Overview's opening prices, which the Initial migration inserts: $2.00 a movie and a $1.00 late fee.
    private static readonly Prices Opening = new(200, 100);

    [Fact]
    public async Task GetAsync_FirstStart_GivesTheOpeningPrices()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var prices = await StorePricesFor(rewind).GetAsync(ct);

        Assert.Equal(Opening, prices);
    }

    [Fact]
    public async Task SaveAsync_TwoPrices_SavesThemInCents()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var change = await StorePricesFor(rewind).SaveAsync("2.50", "1.25", ct);

        Assert.Equal(new PriceChange(PriceChangeStatus.Saved, new Prices(250, 125)), change);
        Assert.Equal(new Prices(250, 125), await SavedPricesAsync(rewind, ct));
    }

    // Build Plan 3.1's Done when: a price changed in the Store Manager is still there after Rewind restarts.
    [Fact]
    public async Task GetAsync_AfterARestart_GivesThePricesSavedBefore()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await StorePricesFor(rewind).SaveAsync("3", ".50", ct);
        await rewind.StartAsync(ct); // a second start, as after Shut Down

        var prices = await StorePricesFor(rewind).GetAsync(ct);

        Assert.Equal(new Prices(300, 50), prices);
    }

    // 100 and 200 are $100.00 and $200.00: past the limit, so a price typed without its point, such as
    // 200 for 2.00, is refused rather than saved.
    [Theory]
    [InlineData("two")]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("2.505")]
    [InlineData("100")]
    [InlineData("200")]
    public async Task SaveAsync_RentalPriceNotAPrice_SavesNothing(string rentalPrice)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var change = await StorePricesFor(rewind).SaveAsync(rentalPrice, "1.25", ct);

        Assert.Equal(new PriceChange(PriceChangeStatus.RentalPriceNotValid, null), change);
        Assert.Equal(Opening, await SavedPricesAsync(rewind, ct));
    }

    [Theory]
    [InlineData("one")]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("1.005")]
    [InlineData("100")]
    public async Task SaveAsync_LateFeeNotAPrice_SavesNothing(string lateFee)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var change = await StorePricesFor(rewind).SaveAsync("2.50", lateFee, ct);

        Assert.Equal(new PriceChange(PriceChangeStatus.LateFeeNotValid, null), change);
        Assert.Equal(Opening, await SavedPricesAsync(rewind, ct));
    }

    // The Prices tab puts the rental price first, so that's the one to fix first.
    [Fact]
    public async Task SaveAsync_NeitherIsAPrice_SaysTheRentalPrice()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var change = await StorePricesFor(rewind).SaveAsync("two", "one", ct);

        Assert.Equal(PriceChangeStatus.RentalPriceNotValid, change.Status);
    }

    // From free to the most the Store Manager can set.
    [Theory]
    [InlineData("0", 0)]
    [InlineData("99.99", StoreSettings.MaxPriceCents)]
    public async Task SaveAsync_PriceAtALimit_SavesIt(string typed, int cents)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        await StorePricesFor(rewind).SaveAsync(typed, typed, ct);

        Assert.Equal(new Prices(cents, cents), await SavedPricesAsync(rewind, ct));
    }

    // OBS-3: the database keeps only the prices as they are now, so the log is where a change shows,
    // and only the price that changed.
    [Fact]
    public async Task SaveAsync_ChangedPrices_LogsEachChange()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var log = new ListLogger<StorePrices>();
        var prices = new StorePrices(rewind.DbFactory, log);

        await prices.SaveAsync("2.50", "1.25", ct);
        await prices.SaveAsync("2.50", "1.50", ct);

        Assert.Equal(
            [
                (LogLevel.Information, "Rental price changed from $2.00 to $2.50", (Exception?)null),
                (LogLevel.Information, "Late fee changed from $1.00 to $1.25", null),
                (LogLevel.Information, "Late fee changed from $1.25 to $1.50", null),
            ],
            log.Entries);
    }

    // The Prices tab saves both boxes each time, so a price saved as it was logs nothing. 2 and 1 are
    // the opening prices, typed another way.
    [Fact]
    public async Task SaveAsync_PricesAsSaved_LogsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var log = new ListLogger<StorePrices>();

        var change = await new StorePrices(rewind.DbFactory, log).SaveAsync("2", "1", ct);

        Assert.Equal(new PriceChange(PriceChangeStatus.Saved, Opening), change);
        Assert.Empty(log.Entries);
    }

    // ADR-0006: a sheet of labels made between the read and the save spends its numbers, and the save
    // writes only the prices, so the next sheet still starts after that one. Writing back the number
    // read before the sheet would print two labels with the same number.
    [Fact]
    public async Task SaveAsync_SheetMadeMeanwhile_KeepsItsNumbers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        await new StorePrices(new SheetMadeJustBeforeEachSave(rewind), NullLogger<StorePrices>.Instance).SaveAsync("3", "1", ct);

        await using var db = rewind.OpenDb();
        var settings = await db.StoreSettings.AsNoTracking().SingleAsync(ct);
        Assert.Equal((300, StoreSettings.FirstNumber + LabelSheets.LabelsPerSheet), (settings.RentalPriceCents, settings.NextStoreLabelNumber));
    }

    [Fact]
    public async Task SaveAsync_NullRentalPrice_IsRefused()
    {
        // NET-1: null on purpose, past the compiler's check, since this tests what SaveAsync does with one.
        var save = () => WithNoDatabase().SaveAsync(null!, "1.00", TestContext.Current.CancellationToken);

        var refusal = await Assert.ThrowsAsync<ArgumentNullException>(save);
        Assert.Equal("rentalPrice", refusal.ParamName);
    }

    [Fact]
    public async Task SaveAsync_NullLateFee_IsRefused()
    {
        // NET-1: as above.
        var save = () => WithNoDatabase().SaveAsync("2.00", null!, TestContext.Current.CancellationToken);

        var refusal = await Assert.ThrowsAsync<ArgumentNullException>(save);
        Assert.Equal("lateFee", refusal.ParamName);
    }

    private static StorePrices StorePricesFor(TempRewind rewind) => new(rewind.DbFactory, NullLogger<StorePrices>.Instance);

    // For what's answered before the database is opened, such as a null.
    private static StorePrices WithNoDatabase() => new(new NoDatabase(), NullLogger<StorePrices>.Instance);

    // The prices as the database holds them.
    private static async Task<Prices> SavedPricesAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        return await db.StoreSettings
            .AsNoTracking()
            .Select(s => new Prices(s.RentalPriceCents, s.LateFeeCents))
            .SingleAsync(cancellationToken);
    }

    private sealed class NoDatabase : IDbContextFactory<RewindDb>
    {
        public RewindDb CreateDbContext() => throw new InvalidOperationException("The prices opened the database.");
    }

    // Contexts each of whose saves a sheet of labels beats: just before the save, the next label number
    // moves on by a sheet, through a context of its own, as LabelSheets would move it.
    private sealed class SheetMadeJustBeforeEachSave(TempRewind rewind) : IDbContextFactory<RewindDb>
    {
        public RewindDb CreateDbContext() =>
            new(new DbContextOptionsBuilder<RewindDb>()
                .UseSqlite(RewindDb.ConnectionStringFor(rewind.Paths.Database))
                .AddInterceptors(new MakeASheetFirst(rewind))
                .Options);
    }

    private sealed class MakeASheetFirst(TempRewind rewind) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await using var db = rewind.OpenDb();
            var settings = await db.StoreSettings.SingleAsync(cancellationToken);
            settings.NextStoreLabelNumber += LabelSheets.LabelsPerSheet;
            await db.SaveChangesAsync(cancellationToken);
            return result;
        }
    }
}
