using Microsoft.EntityFrameworkCore;
using Rewind.Core.Checkout;
using Rewind.Core.Data;
using Rewind.Core.Members;
using Rewind.Core.Returns;
using Rewind.Core.Tests.Checkout;

namespace Rewind.Core.Tests.Returns;

public sealed class ViewingTests
{
    // Midnight on the register's clock, the night SaleParts' rentals are from, since rentals are due back then.
    private static readonly DateTime BackThatNight = new(2026, 10, 24, 4, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(Viewing.MinStars)]
    [InlineData(Viewing.MaxStars)]
    public async Task SaveChanges_StarsAtTheirLimits_AreSaved(int stars)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var item = await AddReturnedItemAsync(db, parts, ct);
        db.WatchLog.Add(Watched(item, parts.MemberId, stars));

        await db.SaveChangesAsync(ct);

        Assert.Equal(stars, (await db.WatchLog.AsNoTracking().SingleAsync(ct)).Stars);
    }

    // DATA-3: a skipped rating is no stars, never 0.
    [Fact]
    public async Task SaveChanges_StarsSkipped_AreSavedAsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var item = await AddReturnedItemAsync(db, parts, ct);
        db.WatchLog.Add(Watched(item, parts.MemberId, stars: null));

        await db.SaveChangesAsync(ct);

        Assert.Null((await db.WatchLog.AsNoTracking().SingleAsync(ct)).Stars);
    }

    [Theory]
    [InlineData(Viewing.MinStars - 1)]
    [InlineData(Viewing.MaxStars + 1)]
    public async Task SaveChanges_StarsOutOfRange_AreRefused(int stars)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var item = await AddReturnedItemAsync(db, parts, ct);
        db.WatchLog.Add(Watched(item, parts.MemberId, stars));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    // Everyone who watched gets a row, each with their own stars.
    [Fact]
    public async Task SaveChanges_TwoMembersOnOneItem_AreSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var item = await AddReturnedItemAsync(db, parts, ct);
        var mom = await AddMomAsync(db, ct);
        db.WatchLog.AddRange(Watched(item, parts.MemberId, 5), Watched(item, mom, 3));

        await db.SaveChangesAsync(ct);

        Assert.Equal(2, await db.WatchLog.CountAsync(ct));
    }

    // DATA-11: one row per person per rental item, so nobody is on one rental's watch log twice.
    [Fact]
    public async Task SaveChanges_OneMemberTwiceOnOneItem_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var item = await AddReturnedItemAsync(db, parts, ct);
        db.WatchLog.Add(Watched(item, parts.MemberId, 5));
        await db.SaveChangesAsync(ct);

        db.WatchLog.Add(Watched(item, parts.MemberId, 4));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Unique, () => db.SaveChangesAsync(ct));
    }

    // The rule is per rental item: a member who rents a movie again can watch it again.
    [Fact]
    public async Task SaveChanges_SameMemberOnAnotherItem_IsSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var first = await AddReturnedItemAsync(db, parts, ct);
        var again = await AddReturnedItemAsync(db, parts, ct);
        db.WatchLog.AddRange(Watched(first, parts.MemberId, 4), Watched(again, parts.MemberId, 5));

        await db.SaveChangesAsync(ct);

        Assert.Equal(2, await db.WatchLog.CountAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_RentalItemThatIsntThere_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        db.WatchLog.Add(Watched(SaleParts.NoSuchId, parts.MemberId, 5));

        await SqliteAssert.RefusesAsync(SqliteConstraint.ForeignKey, () => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_MemberThatIsntThere_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var item = await AddReturnedItemAsync(db, parts, ct);
        db.WatchLog.Add(Watched(item, SaleParts.NoSuchId, 5));

        await SqliteAssert.RefusesAsync(SqliteConstraint.ForeignKey, () => db.SaveChangesAsync(ct));
    }

    // DATA-4: the watch log is the heirloom, so it keeps the rental item and the member it points at.
    [Fact]
    public async Task SaveChanges_DeletingRentalItemWithAViewing_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var item = await AddReturnedItemAsync(db, parts, ct);
        db.WatchLog.Add(Watched(item, parts.MemberId, 5));
        await db.SaveChangesAsync(ct);

        // A fresh context tracks no viewing, so the delete reaches SQLite, and the foreign key refuses it there.
        await using var fresh = rewind.OpenDb();
        fresh.RentalItems.Remove(await fresh.RentalItems.SingleAsync(ct));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Restrict, () => fresh.SaveChangesAsync(ct));
    }

    // Mom watched Dad's rental and rented nothing, so only the watch log can refuse deleting Mom.
    [Fact]
    public async Task SaveChanges_DeletingMemberWithAViewing_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var parts = await SaleParts.AddAsync(db, ct);
        var item = await AddReturnedItemAsync(db, parts, ct);
        var mom = await AddMomAsync(db, ct);
        db.WatchLog.Add(Watched(item, mom, 5));
        await db.SaveChangesAsync(ct);

        await using var fresh = rewind.OpenDb();
        fresh.Members.Remove(await fresh.Members.SingleAsync(m => m.Id == mom, ct));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Restrict, () => fresh.SaveChangesAsync(ct));
    }

    private static Viewing Watched(int rentalItem, int member, int? stars) =>
        new() { RentalItemId = rentalItem, MemberId = member, Stars = stars };

    // Dad's rental of Halloween from the double feature, back that night, since Return asks who watched once
    // the case is back. Each call is another rental of the same case.
    private static async Task<int> AddReturnedItemAsync(RewindDb db, SaleParts parts, CancellationToken cancellationToken)
    {
        var item = new RentalItem
        {
            RentalId = await parts.AddRentalAsync(db, cancellationToken),
            CaseId = parts.CaseId,
            MovieId = parts.MovieId,
            PriceCents = 200,
            ReturnedAt = BackThatNight,
        };
        db.RentalItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return item.Id;
    }

    private static async Task<int> AddMomAsync(RewindDb db, CancellationToken cancellationToken)
    {
        var mom = new Member { Name = "Mom", CardBarcode = "MEM-002" };
        db.Members.Add(mom);
        await db.SaveChangesAsync(cancellationToken);
        return mom.Id;
    }
}
