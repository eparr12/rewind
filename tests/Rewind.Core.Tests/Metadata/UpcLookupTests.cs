using Microsoft.EntityFrameworkCore;
using Rewind.Core.Metadata;

namespace Rewind.Core.Tests.Metadata;

public sealed class UpcLookupTests
{
    private const string FactoryUpc = "036000291452";

    [Fact]
    public async Task SaveChanges_DuplicateUpc_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        db.UpcLookups.Add(Answer(rewind, FactoryUpc, "Halloween (Widescreen DVD)"));
        await db.SaveChangesAsync(ct);

        db.UpcLookups.Add(Answer(rewind, FactoryUpc, "Halloween (Widescreen DVD)"));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Unique, () => db.SaveChangesAsync(ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("03600029145")] // 11 digits
    [InlineData("00036000291452")] // 14 digits
    [InlineData("03600029145A")] // a letter
    public async Task SaveChanges_MalformedUpc_IsRefused(string upc)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        db.UpcLookups.Add(Answer(rewind, upc, "Halloween (Widescreen DVD)"));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_NotFoundAnswer_SavesWithNoTitle()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        // UPCitemdb didn't know the barcode. That's saved too, so it isn't asked again.
        db.UpcLookups.Add(Answer(rewind, FactoryUpc, productTitle: null));
        await db.SaveChangesAsync(ct);

        Assert.Null(await db.UpcLookups.Where(u => u.Upc == FactoryUpc).Select(u => u.ProductTitle).SingleAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_LookedUpAt_ReadsBackAsUtc()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var lookedUpAt = rewind.Time.GetUtcNow().UtcDateTime;

        db.UpcLookups.Add(new UpcLookup { Upc = FactoryUpc, LookedUpAt = lookedUpAt });
        await db.SaveChangesAsync(ct);

        // SQLite hands DateTimes back as Unspecified, and RewindDb's conventions mark them UTC (DATA-8).
        await using var fresh = rewind.OpenDb();
        var readBack = await fresh.UpcLookups.Select(u => u.LookedUpAt).SingleAsync(ct);
        Assert.Equal(DateTimeKind.Utc, readBack.Kind);
        Assert.Equal(lookedUpAt.Ticks, readBack.Ticks);
    }

    // An answer as the UPCitemdb client saves one, timed by the fake clock (TEST-4).
    private static UpcLookup Answer(TempRewind rewind, string upc, string? productTitle) =>
        new() { Upc = upc, ProductTitle = productTitle, LookedUpAt = rewind.Time.GetUtcNow().UtcDateTime };
}
