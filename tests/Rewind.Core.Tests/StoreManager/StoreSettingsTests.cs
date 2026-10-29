using Microsoft.EntityFrameworkCore;
using Rewind.Core.Migrations;
using Rewind.Core.StoreManager;

namespace Rewind.Core.Tests.StoreManager;

public sealed class StoreSettingsTests
{
    [Fact]
    public async Task StartAsync_FirstStart_InsertsOpeningSettings()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();

        await rewind.StartAsync(ct);

        // The Initial migration inserts this row by hand, so nothing else checks it. Its label and card
        // numbers come from AddLabelNumbers' column defaults.
        await using var db = rewind.OpenDb();
        var settings = await db.StoreSettings.AsNoTracking().SingleAsync(ct);
        Assert.Equal("Rewind Video", settings.StoreName);
        Assert.Equal(200, settings.RentalPriceCents);
        Assert.Equal(100, settings.LateFeeCents);
        Assert.Equal((1, 1), (settings.NextStoreLabelNumber, settings.NextMemberCardNumber));
    }

    // The register's row is older than the label and card numbers, so the migration that adds them
    // decides where its first sheets start: RV-00001 and MEM-001, not RV-00000.
    [Fact]
    public async Task StartAsync_DatabaseFromBeforeAddLabelNumbers_StartsCountersAtOne()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        Directory.CreateDirectory(rewind.Paths.Data);
        await using (var before = rewind.OpenDb())
        {
            await before.Database.MigrateAsync(nameof(HoldRatingToUsRatings), ct);
        }

        await rewind.StartAsync(ct);

        await using var db = rewind.OpenDb();
        var settings = await db.StoreSettings.AsNoTracking().SingleAsync(ct);
        Assert.Equal((1, 1), (settings.NextStoreLabelNumber, settings.NextMemberCardNumber));
    }

    [Fact]
    public async Task SaveChanges_SecondRow_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        db.StoreSettings.Add(new StoreSettings { Id = 2, StoreName = "Rewind Video 2" });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    // DATA-11: the database holds each price to what the Store Manager can set.
    [Theory]
    [InlineData(-1)]
    [InlineData(StoreSettings.MaxPriceCents + 1)]
    public async Task SaveChanges_RentalPriceOutOfRange_IsRefused(int cents)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var settings = await db.StoreSettings.SingleAsync(ct);

        settings.RentalPriceCents = cents;

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(StoreSettings.MaxPriceCents + 1)]
    public async Task SaveChanges_LateFeeOutOfRange_IsRefused(int cents)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var settings = await db.StoreSettings.SingleAsync(ct);

        settings.LateFeeCents = cents;

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    // Free, and the most the Store Manager can set, are both prices.
    [Theory]
    [InlineData(0)]
    [InlineData(StoreSettings.MaxPriceCents)]
    public async Task SaveChanges_PricesAtALimit_AreSaved(int cents)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using (var db = rewind.OpenDb())
        {
            var settings = await db.StoreSettings.SingleAsync(ct);
            settings.RentalPriceCents = cents;
            settings.LateFeeCents = cents;
            await db.SaveChangesAsync(ct);
        }

        await using var reread = rewind.OpenDb();
        var saved = await reread.StoreSettings.AsNoTracking().SingleAsync(ct);
        Assert.Equal((cents, cents), (saved.RentalPriceCents, saved.LateFeeCents));
    }

    // AddPriceChecks rebuilds the table, so the register's row has to come through it as it was: the
    // family's prices, and the numbers the next sheets start at, which must never go back (ADR-0006).
    [Fact]
    public async Task StartAsync_DatabaseFromBeforeAddPriceChecks_KeepsItsRow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        Directory.CreateDirectory(rewind.Paths.Data);
        await using (var before = rewind.OpenDb())
        {
            await before.Database.MigrateAsync(nameof(AddMemberChecks), ct);
            var row = await before.StoreSettings.SingleAsync(ct);
            (row.RentalPriceCents, row.LateFeeCents, row.NextStoreLabelNumber, row.NextMemberCardNumber) = (250, 125, 31, 11);
            await before.SaveChangesAsync(ct);
        }

        await rewind.StartAsync(ct);

        await using var db = rewind.OpenDb();
        var settings = await db.StoreSettings.AsNoTracking().SingleAsync(ct);
        Assert.Equal(
            ("Rewind Video", 250, 125, 31, 11),
            (settings.StoreName, settings.RentalPriceCents, settings.LateFeeCents, settings.NextStoreLabelNumber, settings.NextMemberCardNumber));
    }
}
