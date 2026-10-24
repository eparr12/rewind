using Microsoft.EntityFrameworkCore;
using Rewind.Core.StoreManager;

namespace Rewind.Core.Tests.StoreManager;

public sealed class StoreSettingsTests
{
    [Fact]
    public async Task StartAsync_FirstStart_InsertsOpeningPrices()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();

        await rewind.StartAsync(ct);

        // The Initial migration inserts this row by hand, so nothing else checks it.
        await using var db = rewind.OpenDb();
        var settings = await db.StoreSettings.AsNoTracking().SingleAsync(ct);
        Assert.Equal("Rewind Video", settings.StoreName);
        Assert.Equal(200, settings.RentalPriceCents);
        Assert.Equal(100, settings.LateFeeCents);
    }

    [Fact]
    public async Task SaveChanges_SecondRow_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        db.StoreSettings.Add(new StoreSettings { Id = 2, StoreName = "Rewind Video 2" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }
}
