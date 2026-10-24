using Microsoft.EntityFrameworkCore;
using Rewind.Core.Catalog;

namespace Rewind.Core.Tests.Catalog;

public sealed class CastMemberTests
{
    [Fact]
    public async Task SaveChanges_DuplicateBillingOrder_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var halloween = new Movie { Title = "Halloween" };
        halloween.Cast.Add(new CastMember { Name = "Jamie Lee Curtis", BillingOrder = 0 });
        db.Movies.Add(halloween);
        await db.SaveChangesAsync(ct);

        halloween.Cast.Add(new CastMember { Name = "Donald Pleasence", BillingOrder = 0 });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Unique, () => db.SaveChangesAsync(ct));
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
        var halloween = new Movie { Title = "Halloween" };
        halloween.Cast.Add(new CastMember { Name = name, BillingOrder = 0 });

        db.Movies.Add(halloween);

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_ReplacingWholeCast_SavesInOneCall()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var jaws = new Movie { TmdbId = 578, Title = "Jaws" };
        jaws.Cast.Add(new CastMember { Name = "Roy Scheider", BillingOrder = 0 });
        jaws.Cast.Add(new CastMember { Name = "Robert Shaw", BillingOrder = 1 });
        db.Movies.Add(jaws);
        await db.SaveChangesAsync(ct);

        // As a refresh replaces a cast. Every foreign key is Restrict (DATA-4), so Cast.Clear()
        // would only cut the old rows loose and SaveChanges would throw; they're deleted instead.
        // EF Core runs the deletes first, so the new rows can take the same places in the billing.
        db.MovieCast.RemoveRange(jaws.Cast);
        jaws.Cast.Add(new CastMember { Name = "Richard Dreyfuss", BillingOrder = 0 });
        jaws.Cast.Add(new CastMember { Name = "Lorraine Gary", BillingOrder = 1 });
        await db.SaveChangesAsync(ct);

        await using var fresh = rewind.OpenDb();
        string[] expected = ["Richard Dreyfuss", "Lorraine Gary"];
        Assert.Equal(expected, await fresh.MovieCast.OrderBy(c => c.BillingOrder).Select(c => c.Name).ToListAsync(ct));
    }
}
