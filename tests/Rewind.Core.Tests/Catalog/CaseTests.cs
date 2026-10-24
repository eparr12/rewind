using Microsoft.EntityFrameworkCore;
using Rewind.Core.Catalog;

namespace Rewind.Core.Tests.Catalog;

public sealed class CaseTests
{
    private const string FactoryUpc = "036000291452";

    [Fact]
    public async Task SaveChanges_DuplicateStoreLabel_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        db.Cases.Add(new Case { StoreLabel = "RV-00042" });
        await db.SaveChangesAsync(ct);

        db.Cases.Add(new Case { StoreLabel = "RV-00042" });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Unique, () => db.SaveChangesAsync(ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("RV-0042")]
    [InlineData("rv-00042")]
    public async Task SaveChanges_MalformedStoreLabel_IsRefused(string storeLabel)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        db.Cases.Add(new Case { StoreLabel = storeLabel });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    [Theory]
    [InlineData("03600029145")] // 11 digits
    [InlineData("00036000291452")] // 14 digits
    [InlineData("03600029145A")] // a letter
    public async Task SaveChanges_MalformedFactoryUpc_IsRefused(string factoryUpc)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        db.Cases.Add(new Case { StoreLabel = "RV-00001", FactoryUpc = factoryUpc });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_TwoCasesShareFactoryUpc_SavesBoth()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var halloween = new Movie { Title = "Halloween" };
        db.Cases.Add(new Case { StoreLabel = "RV-00001", FactoryUpc = FactoryUpc, Movies = { halloween } });
        await db.SaveChangesAsync(ct);

        // A second copy of the same edition: the same movie, with the same barcode on the case.
        db.Cases.Add(new Case { StoreLabel = "RV-00002", FactoryUpc = FactoryUpc, Movies = { halloween } });
        await db.SaveChangesAsync(ct);

        Assert.Equal(2, await db.Cases.CountAsync(c => c.FactoryUpc == FactoryUpc, ct));
    }

    [Fact]
    public async Task SaveChanges_UnknownStatus_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        // EF Core stores a number outside the enum as text, here "99".
        db.Cases.Add(new Case { StoreLabel = "RV-00042", Status = (CaseStatus)99 });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }
}
