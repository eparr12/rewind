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
}
