using Microsoft.EntityFrameworkCore;
using Rewind.Core.Checkout;
using Rewind.Core.Migrations;
using Rewind.Core.StoreManager;

namespace Rewind.Core.Tests.Checkout;

public sealed class RentalItemTests
{
    private const int RentalPrice = 200;

    // Midnight on the register's clock, the night SaleParts' rentals are from, since rentals are due back then.
    private static readonly DateTime BackThatNight = new(2026, 10, 24, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SaveChanges_MovieInItsCase_IsSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var rental = await parts.AddRentalAsync(db, ct);
        db.RentalItems.Add(Item(rental, parts.CaseId, parts.SecondMovieId));

        await db.SaveChangesAsync(ct);

        Assert.Equal(parts.SecondMovieId, (await db.RentalItems.AsNoTracking().SingleAsync(ct)).MovieId);
    }

    // DATA-11: the pair points at CaseMovies, so a box-set pick can only be a movie that's in that case.
    [Fact]
    public async Task SaveChanges_MovieNotInItsCase_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var rental = await parts.AddRentalAsync(db, ct);
        db.RentalItems.Add(Item(rental, parts.CaseId, parts.MovieInNoCaseId));

        await SqliteAssert.RefusesAsync(SqliteConstraint.ForeignKey, () => db.SaveChangesAsync(ct));
    }

    // A case rents once per sale, even with another movie picked from it, which CaseMovies would allow. The
    // first item is back, so only this rule can refuse the second: with both out, one open rental per case
    // would refuse it too.
    [Fact]
    public async Task SaveChanges_SecondItemFromOneCase_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var rental = await parts.AddRentalAsync(db, ct);
        db.RentalItems.Add(Item(rental, parts.CaseId, parts.MovieId, returnedAt: BackThatNight));
        await db.SaveChangesAsync(ct);

        db.RentalItems.Add(Item(rental, parts.CaseId, parts.SecondMovieId));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Unique, () => db.SaveChangesAsync(ct));
    }

    // DATA-11: a case is out on one rental at a time, so it can't go out again until it's back, whichever movie
    // is picked from it. The second item picks the double feature's other movie, which an index on the pair, or
    // on the movie, would let through.
    [Fact]
    public async Task SaveChanges_SecondOpenItemForOneCase_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var first = await parts.AddRentalAsync(db, ct);
        var second = await parts.AddRentalAsync(db, ct);
        db.RentalItems.Add(Item(first, parts.CaseId, parts.MovieId));
        await db.SaveChangesAsync(ct);

        db.RentalItems.Add(Item(second, parts.CaseId, parts.SecondMovieId));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Unique, () => db.SaveChangesAsync(ct));
    }

    // The same case rents again once it's back, so the rule is per sale, and per case only while it's out.
    [Fact]
    public async Task SaveChanges_SameCaseOnAnotherRentalOnceItsBack_IsSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var first = await parts.AddRentalAsync(db, ct);
        var second = await parts.AddRentalAsync(db, ct);
        db.RentalItems.Add(Item(first, parts.CaseId, parts.MovieId, returnedAt: BackThatNight));
        db.RentalItems.Add(Item(second, parts.CaseId, parts.MovieId));

        await db.SaveChangesAsync(ct);

        Assert.Equal(2, await db.RentalItems.CountAsync(ct));
    }

    // The other way to get a case out twice. The index sees updates too, so a return can't be undone while the
    // case is out again.
    [Fact]
    public async Task SaveChanges_ClearingAReturnWhileTheCaseIsOutAgain_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var first = await parts.AddRentalAsync(db, ct);
        var second = await parts.AddRentalAsync(db, ct);
        var returned = Item(first, parts.CaseId, parts.MovieId, returnedAt: BackThatNight);
        db.RentalItems.AddRange(returned, Item(second, parts.CaseId, parts.MovieId));
        await db.SaveChangesAsync(ct);

        returned.ReturnedAt = null;

        await SqliteAssert.RefusesAsync(SqliteConstraint.Unique, () => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_ReturnedAt_ReadsBackAsUtc()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var rental = await parts.AddRentalAsync(db, ct);
        var returnedAt = rewind.Time.GetUtcNow().UtcDateTime;

        db.RentalItems.Add(Item(rental, parts.CaseId, parts.MovieId, returnedAt: returnedAt));
        await db.SaveChangesAsync(ct);

        // SQLite hands DateTimes back as Unspecified, and RewindDb's conventions mark them UTC (DATA-8).
        await using var fresh = rewind.OpenDb();
        var readBack = await fresh.RentalItems.Select(i => i.ReturnedAt).SingleAsync(ct);
        Assert.Equal(DateTimeKind.Utc, readBack?.Kind);
        Assert.Equal(returnedAt.Ticks, readBack?.Ticks);
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
        var rental = await parts.AddRentalAsync(db, ct);
        db.RentalItems.Add(Item(rental, parts.CaseId, parts.MovieId, priceCents));

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
        var rental = await parts.AddRentalAsync(db, ct);
        db.RentalItems.Add(Item(rental, parts.CaseId, parts.MovieId, priceCents));

        await db.SaveChangesAsync(ct);

        Assert.Equal(priceCents, (await db.RentalItems.AsNoTracking().SingleAsync(ct)).PriceCents);
    }

    [Fact]
    public async Task SaveChanges_RentalThatIsntThere_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        db.RentalItems.Add(Item(SaleParts.NoSuchId, parts.CaseId, parts.MovieId));

        await SqliteAssert.RefusesAsync(SqliteConstraint.ForeignKey, () => db.SaveChangesAsync(ct));
    }

    // DATA-4: what was rented is history, so it keeps the rental and the case's movie it points at.
    [Fact]
    public async Task SaveChanges_DeletingRentalWithAnItem_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddItemAsync(rewind, ct);

        // A fresh context tracks no item, so the delete reaches SQLite, and the foreign key refuses it there.
        await using var fresh = rewind.OpenDb();
        fresh.Rentals.Remove(await fresh.Rentals.SingleAsync(ct));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Restrict, () => fresh.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_DeletingCaseMovieOnAnItem_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var (caseId, movieId) = await AddItemAsync(rewind, ct);

        await using var fresh = rewind.OpenDb();
        fresh.CaseMovies.Remove(await fresh.CaseMovies.SingleAsync(cm => cm.CaseId == caseId && cm.MovieId == movieId, ct));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Restrict, () => fresh.SaveChangesAsync(ct));
    }

    // The register's rentals are older than returns. A case that's out as Rewind migrates stays out: its item
    // keeps everything it had and has no return time, so the case's return finds it.
    [Fact]
    public async Task StartAsync_DatabaseFromBeforeReturns_KeepsOpenItemWithNoReturnTime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        Directory.CreateDirectory(rewind.Paths.Data);
        await using (var before = rewind.OpenDb())
        {
            await before.Database.MigrateAsync(nameof(AddSaleTables), ct);
            await before.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Members" ("Id", "Name", "CardBarcode", "IsActive", "Bag") VALUES (1, 'Dad', 'MEM-001', 1, '');
                INSERT INTO "BusinessDays" ("Id", "OpenedAt") VALUES (1, '2026-10-24 02:15:00');
                INSERT INTO "Movies" ("Id", "Title") VALUES (1, 'Halloween');
                INSERT INTO "Cases" ("Id", "StoreLabel", "Status") VALUES (1, 'RV-00042', 'CheckedOut');
                INSERT INTO "CaseMovies" ("CaseId", "MovieId") VALUES (1, 1);
                INSERT INTO "Rentals" ("Id", "MemberId", "BusinessDayId", "RentedAt", "TotalCents", "CashTenderedCents", "ChangeCents")
                VALUES (1, 1, 1, '2026-10-24 02:15:00', 200, 500, 300);
                INSERT INTO "RentalItems" ("Id", "RentalId", "CaseId", "MovieId", "PriceCents") VALUES (7, 1, 1, 1, 200);
                """,
                ct);
        }

        await rewind.StartAsync(ct);

        await using var db = rewind.OpenDb();
        var item = await db.RentalItems.AsNoTracking().SingleAsync(ct);
        Assert.Equal((7, 1, 1, 1, 200, (DateTime?)null), (item.Id, item.RentalId, item.CaseId, item.MovieId, item.PriceCents, item.ReturnedAt));
    }

    private static RentalItem Item(int rental, int caseId, int movie, int priceCents = RentalPrice, DateTime? returnedAt = null) =>
        new() { RentalId = rental, CaseId = caseId, MovieId = movie, PriceCents = priceCents, ReturnedAt = returnedAt };

    private static async Task<(int CaseId, int MovieId)> AddItemAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, cancellationToken);
        db.RentalItems.Add(Item(await parts.AddRentalAsync(db, cancellationToken), parts.CaseId, parts.MovieId));
        await db.SaveChangesAsync(cancellationToken);
        return (parts.CaseId, parts.MovieId);
    }
}
