using Microsoft.EntityFrameworkCore;
using Rewind.Core.Members;
using Rewind.Core.Migrations;

namespace Rewind.Core.Tests.Members;

public sealed class MemberTests
{
    [Fact]
    public async Task SaveChanges_DuplicateCardBarcode_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        db.Members.Add(new Member { Name = "Dad", CardBarcode = "MEM-001" });
        await db.SaveChangesAsync(ct);

        db.Members.Add(new Member { Name = "Mom", CardBarcode = "MEM-001" });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Unique, () => db.SaveChangesAsync(ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("MEM-01")]
    [InlineData("mem-001")]
    [InlineData("RV-00042")]
    public async Task SaveChanges_MalformedCardBarcode_IsRefused(string cardBarcode)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        db.Members.Add(new Member { Name = "Dad", CardBarcode = cardBarcode });

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

        db.Members.Add(new Member { Name = name, CardBarcode = "MEM-001" });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    // DATA-11: what ShuffleBag.Draw refuses, the database refuses too.
    [Theory]
    [InlineData("CCCCC")] // more than a full bag
    [InlineData("OO")] // two Overdue
    [InlineData("COCO")] // two Overdue, apart
    [InlineData("CX")] // not a card
    [InlineData("c")] // not in capitals
    public async Task SaveChanges_BagOutsideTheRule_IsRefused(string bag)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        db.Members.Add(new Member { Name = "Dad", CardBarcode = "MEM-001", Bag = bag });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    [Theory]
    [InlineData("")] // spent
    [InlineData("O")]
    [InlineData("CCO")]
    [InlineData("CCCO")] // a full bag
    public async Task SaveChanges_BagWithinTheRule_IsSaved(string bag)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        db.Members.Add(new Member { Name = "Dad", CardBarcode = "MEM-001", Bag = bag });

        await db.SaveChangesAsync(ct);

        Assert.Equal(bag, await db.Members.AsNoTracking().Select(m => m.Bag).SingleAsync(ct));
    }

    // A new member's first draw fills their bag, like anyone else's.
    [Fact]
    public async Task SaveChanges_NewMember_StartsWithASpentBag()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        db.Members.Add(new Member { Name = "Dad", CardBarcode = "MEM-001" });

        await db.SaveChangesAsync(ct);

        Assert.Equal("", await db.Members.AsNoTracking().Select(m => m.Bag).SingleAsync(ct));
    }

    // The register's members are older than the bag. Each keeps everything they had and starts with a spent
    // bag, through both the migration that adds the column and the one that rebuilds the table for its CHECK.
    [Fact]
    public async Task StartAsync_DatabaseFromBeforeTheBag_KeepsMembersWithEmptyBags()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        Directory.CreateDirectory(rewind.Paths.Data);
        await using (var before = rewind.OpenDb())
        {
            await before.Database.MigrateAsync(nameof(AddBusinessDays), ct);
            await before.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"Members\" (\"Id\", \"Name\", \"CardBarcode\", \"IsActive\") VALUES (7, 'Dad', 'MEM-001', 0)",
                ct);
        }

        await rewind.StartAsync(ct);

        await using var db = rewind.OpenDb();
        var member = await db.Members.AsNoTracking().SingleAsync(ct);
        Assert.Equal((7, "Dad", "MEM-001", false, ""), (member.Id, member.Name, member.CardBarcode, member.IsActive, member.Bag));
    }
}
