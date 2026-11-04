using Microsoft.EntityFrameworkCore;
using Rewind.Core.Checkout;

namespace Rewind.Core.Tests.Checkout;

public sealed class RentalTests
{
    // DATA-7: Checkout saves all of this in one save, and only the navigations can link new items and lines
    // to a rental that has no key yet. The figures are Build Plan 3.3's: a box-set pick at $2.00, popcorn at
    // $1.00 and a late fee at $1.00 come to $4.00, and $10.00 leaves $6.00.
    [Fact]
    public async Task SaveChanges_NewRentalWithItsItemsAndLines_SavesThemTogether()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var draw = await parts.AddDrawAsync(db, ct);
        var rental = parts.NewRental(totalCents: 400, cashTenderedCents: 1000);
        rental.Items.Add(new RentalItem { CaseId = parts.CaseId, MovieId = parts.MovieId, PriceCents = 200 });
        rental.Lines.Add(new SaleLine { Name = "Popcorn", PriceCents = 100, ConcessionItemId = parts.SnackId });
        rental.Lines.Add(new SaleLine { Name = "Late fee: Halloween (1978)", PriceCents = 100, LateFeeDrawId = draw });
        db.Rentals.Add(rental);

        await db.SaveChangesAsync(ct);

        await using var fresh = rewind.OpenDb();
        var saved = await fresh.Rentals.AsNoTracking().Include(r => r.Items).Include(r => r.Lines).SingleAsync(ct);
        Assert.Equal((600, 1, 2), (saved.ChangeCents, saved.Items.Count, saved.Lines.Count));
    }

    [Fact]
    public async Task SaveChanges_ChangeThatIsntCashLessTotal_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var rental = parts.NewRental(totalCents: 400, cashTenderedCents: 1000);
        rental.ChangeCents = 500;
        db.Rentals.Add(rental);

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_CashUnderTheTotal_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        db.Rentals.Add(parts.NewRental(totalCents: 400, cashTenderedCents: 399));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    // Enough cash for it, and change that adds up, so only the total's own rule can refuse this.
    [Fact]
    public async Task SaveChanges_TotalBelowZero_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        db.Rentals.Add(parts.NewRental(totalCents: -1, cashTenderedCents: 0));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_CashExactlyTheTotal_IsSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        db.Rentals.Add(parts.NewRental(totalCents: 400, cashTenderedCents: 400));

        await db.SaveChangesAsync(ct);

        Assert.Equal(0, (await db.Rentals.AsNoTracking().SingleAsync(ct)).ChangeCents);
    }

    // The Store Manager can set the rental price to nothing, so a sale can come to nothing.
    [Fact]
    public async Task SaveChanges_TotalOfNothing_IsSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        db.Rentals.Add(parts.NewRental(totalCents: 0, cashTenderedCents: 0));

        await db.SaveChangesAsync(ct);

        Assert.Equal(0, (await db.Rentals.AsNoTracking().SingleAsync(ct)).TotalCents);
    }

    [Fact]
    public async Task SaveChanges_MemberThatIsntThere_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var rental = parts.NewRental();
        rental.MemberId = SaleParts.NoSuchId;
        db.Rentals.Add(rental);

        await SqliteAssert.RefusesAsync(SqliteConstraint.ForeignKey, () => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_NightThatIsntThere_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var rental = parts.NewRental();
        rental.BusinessDayId = SaleParts.NoSuchId;
        db.Rentals.Add(rental);

        await SqliteAssert.RefusesAsync(SqliteConstraint.ForeignKey, () => db.SaveChangesAsync(ct));
    }

    // DATA-4: a rental is history, so it keeps the member and the night it points at.
    [Fact]
    public async Task SaveChanges_DeletingMemberWithARental_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        await parts.AddRentalAsync(db, ct);

        // A fresh context tracks no rental, so the delete reaches SQLite, and the foreign key refuses it there.
        await using var fresh = rewind.OpenDb();
        fresh.Members.Remove(await fresh.Members.SingleAsync(ct));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Restrict, () => fresh.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_DeletingNightWithARental_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        await parts.AddRentalAsync(db, ct);

        await using var fresh = rewind.OpenDb();
        fresh.BusinessDays.Remove(await fresh.BusinessDays.SingleAsync(ct));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Restrict, () => fresh.SaveChangesAsync(ct));
    }
}
