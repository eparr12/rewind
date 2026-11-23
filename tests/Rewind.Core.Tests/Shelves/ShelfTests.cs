using Microsoft.EntityFrameworkCore;
using Rewind.Core.Data;
using Rewind.Core.Members;
using Rewind.Core.Migrations;
using Rewind.Core.Shelves;

namespace Rewind.Core.Tests.Shelves;

public sealed class ShelfTests
{
    private const int NoSuchMember = 999;

    // Every column a seasonal shelf has, for a member's shelf that would be both kinds.
    private const string NameAndSeason = "NameAndSeason";

    [Fact]
    public void StaffPicksName_MembersName_IsTheirStaffPicks() =>
        Assert.Equal("Dad's Staff Picks", Shelf.StaffPicksName("Dad"));

    // Always the name and 's, as StaffPicksName's summary says, whatever the name ends in.
    [Fact]
    public void StaffPicksName_NameEndingInAnS_StillTakesApostropheS() =>
        Assert.Equal("Chris's Staff Picks", Shelf.StaffPicksName("Chris"));

    [Fact]
    public void StaffPicksName_NullName_IsRefused()
    {
        // NET-1: null on purpose, past the compiler's check, since this tests what StaffPicksName does with one.
        Action name = () => Shelf.StaffPicksName(null!);

        var refusal = Assert.Throws<ArgumentNullException>(name);
        Assert.Equal("memberName", refusal.ParamName);
    }

    // A member's shelf is named for its member, so it has no name or season of its own, and it's always active.
    [Fact]
    public async Task SaveChanges_MembersStaffPicks_IsSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var dad = await AddMemberAsync(db, "MEM-001", ct);
        db.Shelves.Add(new Shelf { MemberId = dad });

        await db.SaveChangesAsync(ct);

        Assert.Equal([StaffPicksOf(dad)], await SavedShelvesAsync(db, ct));
    }

    [Fact]
    public async Task SaveChanges_SeasonalShelfWithANameAndASeason_IsSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        db.Shelves.Add(Halloween());

        await db.SaveChangesAsync(ct);

        Assert.Equal([new SavedShelf(null, "Halloween", 10, 1, 10, 31, true)], await SavedShelvesAsync(db, ct));
    }

    // The Store Manager deactivates a seasonal shelf rather than deleting it (DATA-4). Only a member's shelf is
    // held to being active.
    [Fact]
    public async Task SaveChanges_SeasonalShelfDeactivated_IsSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var halloween = Halloween();
        halloween.IsActive = false;
        db.Shelves.Add(halloween);

        await db.SaveChangesAsync(ct);

        Assert.False(Assert.Single(await SavedShelvesAsync(db, ct)).IsActive);
    }

    // No column says which kind a shelf is, so a shelf with neither a member nor a name is neither kind: with a
    // season, a seasonal shelf with no name, and without one, nothing at all. Each end of its season is a real day or
    // NULL, so only the kind's CHECK can refuse it.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveChanges_ShelfWithNeitherAMemberNorAName_IsRefused(bool withASeason)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        db.Shelves.Add(withASeason ? new Shelf { FromMonth = 10, FromDay = 1, ToMonth = 10, ToDay = 31 } : new Shelf());

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    // A member's shelf is named for its member and has no season, so a name or any one number of a season refuses
    // it, and so does everything a seasonal shelf has, which would make it both kinds at once. Each number alone is a
    // real day's month or day, so neither end's own CHECK can refuse it.
    [Theory]
    [InlineData(nameof(Shelf.Name))]
    [InlineData(nameof(Shelf.FromMonth))]
    [InlineData(nameof(Shelf.FromDay))]
    [InlineData(nameof(Shelf.ToMonth))]
    [InlineData(nameof(Shelf.ToDay))]
    [InlineData(NameAndSeason)]
    public async Task SaveChanges_MembersShelfWithANameOrASeason_IsRefused(string column)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var dad = await AddMemberAsync(db, "MEM-001", ct);
        var shelf = column switch
        {
            nameof(Shelf.Name) => new Shelf { MemberId = dad, Name = "Dad's Picks" },
            nameof(Shelf.FromMonth) => new Shelf { MemberId = dad, FromMonth = 10 },
            nameof(Shelf.FromDay) => new Shelf { MemberId = dad, FromDay = 1 },
            nameof(Shelf.ToMonth) => new Shelf { MemberId = dad, ToMonth = 10 },
            nameof(Shelf.ToDay) => new Shelf { MemberId = dad, ToDay = 31 },
            NameAndSeason => new Shelf { MemberId = dad, Name = "Halloween", FromMonth = 10, FromDay = 1, ToMonth = 10, ToDay = 31 },
            _ => throw new ArgumentOutOfRangeException(nameof(column), column, "No shelf for that column."),
        };

        db.Shelves.Add(shelf);

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    // A seasonal shelf has all four numbers of its season. With one missing, its end's CHECK comes to NULL, which
    // passes, so only the kind's CHECK can refuse it.
    [Theory]
    [InlineData(nameof(Shelf.FromMonth))]
    [InlineData(nameof(Shelf.FromDay))]
    [InlineData(nameof(Shelf.ToMonth))]
    [InlineData(nameof(Shelf.ToDay))]
    public async Task SaveChanges_SeasonalShelfMissingPartOfItsSeason_IsRefused(string column)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var shelf = column switch
        {
            nameof(Shelf.FromMonth) => new Shelf { Name = "Halloween", FromDay = 1, ToMonth = 10, ToDay = 31 },
            nameof(Shelf.FromDay) => new Shelf { Name = "Halloween", FromMonth = 10, ToMonth = 10, ToDay = 31 },
            nameof(Shelf.ToMonth) => new Shelf { Name = "Halloween", FromMonth = 10, FromDay = 1, ToDay = 31 },
            nameof(Shelf.ToDay) => new Shelf { Name = "Halloween", FromMonth = 10, FromDay = 1, ToMonth = 10 },
            _ => throw new ArgumentOutOfRangeException(nameof(column), column, "No shelf for that column."),
        };

        db.Shelves.Add(shelf);

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    // Nothing deactivates a member's shelf by itself: it's hidden while its member is deactivated (ADR-0015).
    [Fact]
    public async Task SaveChanges_MembersShelfNotActive_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var dad = await AddMemberAsync(db, "MEM-001", ct);

        db.Shelves.Add(new Shelf { MemberId = dad, IsActive = false });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    // DATA-11: one Staff Picks shelf for each member. The kind's CHECK holds every other column of a member's shelf to
    // one value, so the two can differ only in their keys.
    [Fact]
    public async Task SaveChanges_SecondStaffPicksForOneMember_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var dad = await AddMemberAsync(db, "MEM-001", ct);
        db.Shelves.Add(new Shelf { MemberId = dad });
        await db.SaveChangesAsync(ct);

        db.Shelves.Add(new Shelf { MemberId = dad });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Unique, () => db.SaveChangesAsync(ct));
    }

    // Seasonal shelves have no member, and SQLite's unique index lets NULLs repeat.
    [Fact]
    public async Task SaveChanges_TwoSeasonalShelves_AreSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        db.Shelves.AddRange(Halloween(), Christmas());

        await db.SaveChangesAsync(ct);

        Assert.Equal(["Halloween", "Christmas"], (await SavedShelvesAsync(db, ct)).Select(s => s.Name));
    }

    // A member's shelf is named for its member, so only a seasonal shelf is held to a name that isn't blank.
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task SaveChanges_SeasonalShelfWithABlankName_IsRefused(string name)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        db.Shelves.Add(new Shelf { Name = name, FromMonth = 10, FromDay = 1, ToMonth = 10, ToDay = 31 });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    // DATA-11: no two seasonal shelves share a name, whatever its letter case. The second differs from the first in
    // everything else, its season and whether it's active too, so only an index on the name alone can refuse it. The
    // first is deactivated, since a deactivated shelf keeps its name, as a deactivated member keeps their card.
    [Theory]
    [InlineData("Halloween")]
    [InlineData("hALLOWEEN")]
    public async Task SaveChanges_SecondSeasonalShelfWithOneName_IsRefused(string name)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var halloween = Halloween();
        halloween.IsActive = false;
        db.Shelves.Add(halloween);
        await db.SaveChangesAsync(ct);

        db.Shelves.Add(new Shelf { Name = name, FromMonth = 9, FromDay = 15, ToMonth = 11, ToDay = 2 });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Unique, () => db.SaveChangesAsync(ct));
    }

    // NOCASE folds letter case for A to Z only, so two names that differ only in an accented letter's case are two
    // names. That's the limit #100 holds, pinned as it is.
    [Fact]
    public async Task SaveChanges_NamesThatDifferOnlyInAnAccentedLettersCase_AreBothSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        db.Shelves.AddRange(
            new Shelf { Name = "Día de Muertos", FromMonth = 10, FromDay = 31, ToMonth = 11, ToDay = 2 },
            new Shelf { Name = "DÍA DE MUERTOS", FromMonth = 10, FromDay = 31, ToMonth = 11, ToDay = 2 });

        await db.SaveChangesAsync(ct);

        Assert.Equal(2, await db.Shelves.CountAsync(ct));
    }

    // Each end of a season is a real day, and a season has no year, so February's last is its 29th. Both ends are on
    // the day, so each end's CHECK takes it.
    [Theory]
    [InlineData(1, 31)]
    [InlineData(2, 29)]
    [InlineData(3, 31)]
    [InlineData(4, 30)]
    [InlineData(5, 31)]
    [InlineData(6, 30)]
    [InlineData(7, 31)]
    [InlineData(8, 31)]
    [InlineData(9, 30)]
    [InlineData(10, 31)]
    [InlineData(11, 30)]
    [InlineData(12, 31)]
    public async Task SaveChanges_SeasonOfAMonthsLastDay_IsSaved(int month, int lastDay)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        db.Shelves.Add(new Shelf { Name = "One day", FromMonth = month, FromDay = lastDay, ToMonth = month, ToDay = lastDay });

        await db.SaveChangesAsync(ct);

        var saved = Assert.Single(await SavedShelvesAsync(db, ct));
        Assert.Equal((month, lastDay, month, lastDay), (saved.FromMonth, saved.FromDay, saved.ToMonth, saved.ToDay));
    }

    // The day after each month's last isn't a day, February's 30th among them, and neither are month 0, month 13 and
    // day 0. Only the season's start is wrong, so only CK_Shelves_From can refuse it.
    [Theory]
    [InlineData(1, 32)]
    [InlineData(2, 30)]
    [InlineData(3, 32)]
    [InlineData(4, 31)]
    [InlineData(5, 32)]
    [InlineData(6, 31)]
    [InlineData(7, 32)]
    [InlineData(8, 32)]
    [InlineData(9, 31)]
    [InlineData(10, 32)]
    [InlineData(11, 31)]
    [InlineData(12, 32)]
    [InlineData(0, 1)]
    [InlineData(13, 1)]
    [InlineData(1, 0)]
    public async Task SaveChanges_SeasonFromADayThatIsntOne_IsRefused(int month, int day)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        db.Shelves.Add(new Shelf { Name = "Halloween", FromMonth = month, FromDay = day, ToMonth = 10, ToDay = 31 });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    // As for the start: only the season's end is wrong, so only CK_Shelves_To can refuse it.
    [Theory]
    [InlineData(1, 32)]
    [InlineData(2, 30)]
    [InlineData(3, 32)]
    [InlineData(4, 31)]
    [InlineData(5, 32)]
    [InlineData(6, 31)]
    [InlineData(7, 32)]
    [InlineData(8, 32)]
    [InlineData(9, 31)]
    [InlineData(10, 32)]
    [InlineData(11, 31)]
    [InlineData(12, 32)]
    [InlineData(0, 1)]
    [InlineData(13, 1)]
    [InlineData(1, 0)]
    public async Task SaveChanges_SeasonToADayThatIsntOne_IsRefused(int month, int day)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        db.Shelves.Add(new Shelf { Name = "Halloween", FromMonth = 10, FromDay = 1, ToMonth = month, ToDay = day });

        await SqliteAssert.RefusesAsync(SqliteConstraint.Check, () => db.SaveChangesAsync(ct));
    }

    // A season's first day may come after its last, for one that runs over New Year.
    [Fact]
    public async Task SaveChanges_SeasonOverNewYear_IsSaved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        db.Shelves.Add(Christmas());

        await db.SaveChangesAsync(ct);

        Assert.Equal([new SavedShelf(null, "Christmas", 12, 1, 1, 6, true)], await SavedShelvesAsync(db, ct));
    }

    [Fact]
    public async Task SaveChanges_MemberThatIsntThere_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        db.Shelves.Add(new Shelf { MemberId = NoSuchMember });

        await SqliteAssert.RefusesAsync(SqliteConstraint.ForeignKey, () => db.SaveChangesAsync(ct));
    }

    // DATA-4: a member's shelf keeps its member. Dad has nothing else that points at him, so only his shelf can
    // refuse the delete.
    [Fact]
    public async Task SaveChanges_DeletingAMemberWithAShelf_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();
        var dad = await AddMemberAsync(db, "MEM-001", ct);
        db.Shelves.Add(new Shelf { MemberId = dad });
        await db.SaveChangesAsync(ct);

        // A fresh context tracks no shelf, so the delete reaches SQLite, and the foreign key refuses it there.
        await using var fresh = rewind.OpenDb();
        fresh.Members.Remove(await fresh.Members.SingleAsync(ct));

        await SqliteAssert.RefusesAsync(SqliteConstraint.Restrict, () => fresh.SaveChangesAsync(ct));
    }

    // The register's members are older than the shelves. The start that migrates the register gives each of them one
    // Staff Picks shelf, Mom too though she's deactivated, since a member's shelf is hidden while they are, not gone.
    // Their keys aren't 1 and 2, so each shelf has to be its own member's.
    [Fact]
    public async Task StartAsync_DatabaseFromBeforeTheShelves_GivesEachMemberOneStaffPicksShelf()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        Directory.CreateDirectory(rewind.Paths.Data);
        await using (var before = rewind.OpenDb())
        {
            await before.Database.MigrateAsync(nameof(AddWatchLog), ct);
            await before.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Members" ("Id", "Name", "CardBarcode", "IsActive", "Bag") VALUES (3, 'Dad', 'MEM-001', 1, '');
                INSERT INTO "Members" ("Id", "Name", "CardBarcode", "IsActive", "Bag") VALUES (7, 'Mom', 'MEM-002', 0, '');
                """,
                ct);
        }

        await rewind.StartAsync(ct);

        await using var db = rewind.OpenDb();
        Assert.Equal([StaffPicksOf(3), StaffPicksOf(7)], await SavedShelvesAsync(db, ct));
    }

    // A member saved straight to the database, with no shelf, for a test to give one.
    private static async Task<int> AddMemberAsync(RewindDb db, string card, CancellationToken cancellationToken)
    {
        var member = new Member { Name = "Dad", CardBarcode = card };
        db.Members.Add(member);
        await db.SaveChangesAsync(cancellationToken);
        return member.Id;
    }

    private static Shelf Halloween() => new() { Name = "Halloween", FromMonth = 10, FromDay = 1, ToMonth = 10, ToDay = 31 };

    // From December 1 to January 6, Twelfth Night, so over New Year.
    private static Shelf Christmas() => new() { Name = "Christmas", FromMonth = 12, FromDay = 1, ToMonth = 1, ToDay = 6 };

    private static SavedShelf StaffPicksOf(int member) => new(member, null, null, null, null, null, true);

    // The shelves as the database holds them, in the order they were added.
    private static Task<List<SavedShelf>> SavedShelvesAsync(RewindDb db, CancellationToken cancellationToken) =>
        db.Shelves
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .Select(s => new SavedShelf(s.MemberId, s.Name, s.FromMonth, s.FromDay, s.ToMonth, s.ToDay, s.IsActive))
            .ToListAsync(cancellationToken);

    private sealed record SavedShelf(int? MemberId, string? Name, int? FromMonth, int? FromDay, int? ToMonth, int? ToDay, bool IsActive);
}
