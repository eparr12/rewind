using Microsoft.EntityFrameworkCore;
using Rewind.Core.StoreManager;

namespace Rewind.Core.Tests.StoreManager;

public sealed class StoreSettingsTests
{
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
