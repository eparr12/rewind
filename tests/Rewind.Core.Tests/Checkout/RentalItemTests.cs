using Microsoft.EntityFrameworkCore;
using Rewind.Core.Checkout;
using Rewind.Core.StoreManager;

namespace Rewind.Core.Tests.Checkout;

public sealed class RentalItemTests
{
    private const int RentalPrice = 200;

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

    // A case rents once per sale, even with another movie picked from it, which CaseMovies would allow.
    [Fact]
    public async Task SaveChanges_SecondItemFromOneCase_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var rental = await parts.AddRentalAsync(db, ct);
        db.RentalItems.Add(Item(rental, parts.CaseId, parts.MovieId));
        await db.SaveChangesAsync(ct);

        db.RentalItems.Add(Item(rental, parts.CaseId, parts.SecondMovieId));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Unique, () => db.SaveChangesAsync(ct));
    }

    // The same case rents again once it's back, so the rule is per sale, not per case.
    [Fact]
    public async Task SaveChanges_SameCaseOnAnotherRental_IsSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        db.RentalItems.Add(Item(await parts.AddRentalAsync(db, ct), parts.CaseId, parts.MovieId));
        db.RentalItems.Add(Item(await parts.AddRentalAsync(db, ct), parts.CaseId, parts.MovieId));

        await db.SaveChangesAsync(ct);

        Assert.Equal(2, await db.RentalItems.CountAsync(ct));
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

    private static RentalItem Item(int rental, int caseId, int movie, int priceCents = RentalPrice) =>
        new() { RentalId = rental, CaseId = caseId, MovieId = movie, PriceCents = priceCents };

    private static async Task<(int CaseId, int MovieId)> AddItemAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, cancellationToken);
        db.RentalItems.Add(Item(await parts.AddRentalAsync(db, cancellationToken), parts.CaseId, parts.MovieId));
        await db.SaveChangesAsync(cancellationToken);
        return (parts.CaseId, parts.MovieId);
    }
}
