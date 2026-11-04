using Microsoft.EntityFrameworkCore;
using Rewind.Core.Checkout;
using Rewind.Core.Metadata;
using Rewind.Core.StoreManager;

namespace Rewind.Core.Tests.Checkout;

public sealed class SaleLineTests
{
    private const int LateFee = 100;

    // The longest name a fee line can have: the longest title Rewind keeps, with its year.
    private static readonly string LongestFeeLine = $"Late fee: {new string('x', TmdbClient.MaxNameLength)} (1978)";

    // DATA-10, the popcorn test in its data form (Build Plan 3.3): a line reads as it was rung up.
    [Fact]
    public async Task SaveChanges_SnackChangesAfterTheSale_LeavesTheLineAsRungUp()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        db.SaleLines.Add(Snack(await parts.AddRentalAsync(db, ct), parts.SnackId));
        await db.SaveChangesAsync(ct);
        var popcorn = await db.ConcessionItems.SingleAsync(ct);
        popcorn.Name = "Buttered popcorn";
        popcorn.PriceCents = 150;

        await db.SaveChangesAsync(ct);

        await using var fresh = rewind.OpenDb();
        var line = await fresh.SaleLines.AsNoTracking().SingleAsync(ct);
        Assert.Equal(("Popcorn", 100), (line.Name, line.PriceCents));
    }

    // ADR-0010: a draw's fee is paid once, whatever the service does.
    [Fact]
    public async Task SaveChanges_SecondFeeLineForOneDraw_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var draw = await parts.AddDrawAsync(db, ct);
        db.SaleLines.Add(Fee(await parts.AddRentalAsync(db, ct), draw));
        await db.SaveChangesAsync(ct);

        db.SaleLines.Add(Fee(await parts.AddRentalAsync(db, ct), draw));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Unique, () => db.SaveChangesAsync(ct));
    }

    // SQLite treats NULLs as distinct in a unique index, so snack lines, with no draw, don't collide.
    [Fact]
    public async Task SaveChanges_TwoSnackLines_AreSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var rental = await parts.AddRentalAsync(db, ct);
        db.SaleLines.AddRange(Snack(rental, parts.SnackId), Snack(rental, parts.SnackId));

        await db.SaveChangesAsync(ct);

        Assert.Equal(2, await db.SaleLines.CountAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_LineWithASnackAndADraw_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var line = Fee(await parts.AddRentalAsync(db, ct), await parts.AddDrawAsync(db, ct));
        line.ConcessionItemId = parts.SnackId;
        db.SaleLines.Add(line);

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_LineWithNeitherASnackNorADraw_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        db.SaleLines.Add(new SaleLine { RentalId = await parts.AddRentalAsync(db, ct), Name = "Popcorn", PriceCents = 100 });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveChanges_BlankName_IsRefused(string name)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var line = Snack(await parts.AddRentalAsync(db, ct), parts.SnackId);
        line.Name = name;
        db.SaleLines.Add(line);

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_LongestFeeLine_IsSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var line = Fee(await parts.AddRentalAsync(db, ct), await parts.AddDrawAsync(db, ct));
        line.Name = LongestFeeLine;
        db.SaleLines.Add(line);

        await db.SaveChangesAsync(ct);

        Assert.Equal(LongestFeeLine, (await db.SaleLines.AsNoTracking().SingleAsync(ct)).Name);
    }

    [Fact]
    public async Task SaveChanges_NameLongerThanTheLongestFeeLine_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var line = Fee(await parts.AddRentalAsync(db, ct), await parts.AddDrawAsync(db, ct));
        line.Name = LongestFeeLine + "x";
        db.SaleLines.Add(line);

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(StoreSettings.MaxPriceCents + 1)]
    public async Task SaveChanges_PriceOutOfRange_IsRefused(int priceCents)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var line = Snack(await parts.AddRentalAsync(db, ct), parts.SnackId);
        line.PriceCents = priceCents;
        db.SaleLines.Add(line);

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(StoreSettings.MaxPriceCents)]
    public async Task SaveChanges_PriceAtItsLimit_IsSaved(int priceCents)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var line = Snack(await parts.AddRentalAsync(db, ct), parts.SnackId);
        line.PriceCents = priceCents;
        db.SaleLines.Add(line);

        await db.SaveChangesAsync(ct);

        Assert.Equal(priceCents, (await db.SaleLines.AsNoTracking().SingleAsync(ct)).PriceCents);
    }

    [Fact]
    public async Task SaveChanges_RentalThatIsntThere_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        db.SaleLines.Add(Snack(SaleParts.NoSuchId, parts.SnackId));

        await SqliteAssert.RefusesAsync(SqliteConstraint.ForeignKey, () => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_SnackThatIsntThere_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        db.SaleLines.Add(Snack(await parts.AddRentalAsync(db, ct), SaleParts.NoSuchId));

        await SqliteAssert.RefusesAsync(SqliteConstraint.ForeignKey, () => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_DrawThatIsntThere_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        db.SaleLines.Add(Fee(await parts.AddRentalAsync(db, ct), SaleParts.NoSuchId));

        await SqliteAssert.RefusesAsync(SqliteConstraint.ForeignKey, () => db.SaveChangesAsync(ct));
    }

    // DATA-4: what was sold is history, so a line keeps the rental, the snack and the draw it points at.
    [Fact]
    public async Task SaveChanges_DeletingRentalWithALine_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddSnackLineAsync(rewind, ct);

        // A fresh context tracks no line, so the delete reaches SQLite, and the foreign key refuses it there.
        await using var fresh = rewind.OpenDb();
        fresh.Rentals.Remove(await fresh.Rentals.SingleAsync(ct));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Restrict, () => fresh.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_DeletingSnackOnALine_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddSnackLineAsync(rewind, ct);

        await using var fresh = rewind.OpenDb();
        fresh.ConcessionItems.Remove(await fresh.ConcessionItems.SingleAsync(ct));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Restrict, () => fresh.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_DeletingDrawWithAFeeLine_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using (var db = rewind.OpenDb())
        {
            var parts = await SaleParts.AddAsync(db, ct);
            db.SaleLines.Add(Fee(await parts.AddRentalAsync(db, ct), await parts.AddDrawAsync(db, ct)));
            await db.SaveChangesAsync(ct);
        }

        await using var fresh = rewind.OpenDb();
        fresh.LateFeeDraws.Remove(await fresh.LateFeeDraws.SingleAsync(ct));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Restrict, () => fresh.SaveChangesAsync(ct));
    }

    private static SaleLine Snack(int rental, int snack) =>
        new() { RentalId = rental, Name = "Popcorn", PriceCents = 100, ConcessionItemId = snack };

    private static SaleLine Fee(int rental, int draw) =>
        new() { RentalId = rental, Name = "Late fee: Halloween (1978)", PriceCents = LateFee, LateFeeDrawId = draw };

    private static async Task AddSnackLineAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, cancellationToken);
        db.SaleLines.Add(Snack(await parts.AddRentalAsync(db, cancellationToken), parts.SnackId));
        await db.SaveChangesAsync(cancellationToken);
    }
}
