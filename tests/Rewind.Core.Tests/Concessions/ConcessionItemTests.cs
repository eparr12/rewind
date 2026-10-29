using Microsoft.EntityFrameworkCore;
using Rewind.Core.Concessions;
using Rewind.Core.StoreManager;

namespace Rewind.Core.Tests.Concessions;

public sealed class ConcessionItemTests
{
    // DATA-11: one item per code, active or not, so a scan of the menu always means one snack.
    [Fact]
    public async Task SaveChanges_DuplicateCode_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        db.ConcessionItems.Add(new ConcessionItem { Name = "Popcorn", Code = "CON-POPCORN", PriceCents = 100 });
        await db.SaveChangesAsync(ct);

        db.ConcessionItems.Add(new ConcessionItem { Name = "Kettle corn", Code = "CON-POPCORN", PriceCents = 125 });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Unique, () => db.SaveChangesAsync(ct));
    }

    // The form Barcodes.Classify reads as a concession, CON- and 1 to 20 capital letters, so the menu never
    // carries a code the register wouldn't know.
    [Theory]
    [InlineData("POPCORN")]
    [InlineData("CON-")]
    [InlineData("CON-popcorn")]
    [InlineData("CON-POP2")]
    [InlineData("CON-POP CORN")]
    [InlineData("CON-ÉCLAIR")]
    [InlineData("CON-ABCDEFGHIJKLMNOPQRSTU")] // 21 letters
    [InlineData("MEM-001")]
    public async Task SaveChanges_NotAConcessionCode_IsRefused(string code)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        db.ConcessionItems.Add(new ConcessionItem { Name = "Popcorn", Code = code, PriceCents = 100 });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SaveChanges_BlankName_IsRefused(string name)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        db.ConcessionItems.Add(new ConcessionItem { Name = name, Code = "CON-POPCORN", PriceCents = 100 });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    // The same limit as the rental price and the late fee.
    [Theory]
    [InlineData(-1)]
    [InlineData(StoreSettings.MaxPriceCents + 1)]
    public async Task SaveChanges_PriceOutOfRange_IsRefused(int cents)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        db.ConcessionItems.Add(new ConcessionItem { Name = "Popcorn", Code = "CON-POPCORN", PriceCents = cents });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    // The longest code, the shortest, and a price at each limit are all an item's.
    [Theory]
    [InlineData("CON-ABCDEFGHIJKLMNOPQRST", 0)]
    [InlineData("CON-A", StoreSettings.MaxPriceCents)]
    public async Task SaveChanges_CodeAndPriceAtTheirLimits_AreSaved(string code, int cents)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using (var db = rewind.OpenDb())
        {
            db.ConcessionItems.Add(new ConcessionItem { Name = "Popcorn", Code = code, PriceCents = cents });
            await db.SaveChangesAsync(ct);
        }

        await using var reread = rewind.OpenDb();
        var saved = await reread.ConcessionItems.AsNoTracking().SingleAsync(ct);
        Assert.Equal((code, cents), (saved.Code, saved.PriceCents));
    }
}
