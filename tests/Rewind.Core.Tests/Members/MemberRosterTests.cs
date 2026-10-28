using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Rewind.Core.Data;
using Rewind.Core.Members;

namespace Rewind.Core.Tests.Members;

public sealed class MemberRosterTests
{
    private const string Card = "MEM-001";

    [Fact]
    public async Task AddAsync_NameAndCard_SavesAnActiveMember()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var change = await Roster(rewind).AddAsync("Grandma", Card, ct);

        var saved = Assert.Single(await SavedMembersAsync(rewind, ct));
        Assert.Equal(new MemberChange(MemberChangeStatus.Saved, saved), change);
        Assert.Equal(("Grandma", Card, true), (saved.Name, saved.CardBarcode, saved.IsActive));
    }

    [Fact]
    public async Task AddAsync_NameWithSpacesAround_SavesItTrimmed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        await Roster(rewind).AddAsync("  Grandma  ", Card, ct);

        Assert.Equal("Grandma", Assert.Single(await SavedMembersAsync(rewind, ct)).Name);
    }

    [Fact]
    public async Task AddAsync_NameLongerThanTheLimit_SavesItCut()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        await Roster(rewind).AddAsync(new string('A', Member.MaxNameLength + 5), Card, ct);

        Assert.Equal(new string('A', Member.MaxNameLength), Assert.Single(await SavedMembersAsync(rewind, ct)).Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddAsync_BlankName_SavesNothing(string name)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var change = await Roster(rewind).AddAsync(name, Card, ct);

        Assert.Equal(new MemberChange(MemberChangeStatus.NoName, null), change);
        Assert.Empty(await SavedMembersAsync(rewind, ct));
    }

    // Barcodes reads a scan in any case, as the scanner's settings may send it.
    [Fact]
    public async Task AddAsync_CardInLowerCase_SavesItInCapitals()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        await Roster(rewind).AddAsync("Grandma", "mem-001", ct);

        Assert.Equal(Card, Assert.Single(await SavedMembersAsync(rewind, ct)).CardBarcode);
    }

    [Theory]
    [InlineData("RV-00042")] // a store label
    [InlineData("036000291452")] // a factory UPC
    [InlineData("CON-POPCORN")] // a snack
    [InlineData("MEM-01")] // a digit short
    public async Task AddAsync_NotAMemberCard_SavesNothing(string barcode)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var change = await Roster(rewind).AddAsync("Grandma", barcode, ct);

        Assert.Equal(new MemberChange(MemberChangeStatus.NotAMemberCard, null), change);
        Assert.Empty(await SavedMembersAsync(rewind, ct));
    }

    [Fact]
    public async Task AddAsync_CardOfAnotherMember_IsCardInUse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var mom = await AddMemberAsync(rewind, "Mom", Card, ct);

        var change = await Roster(rewind).AddAsync("Grandma", Card, ct);

        Assert.Equal(new MemberChange(MemberChangeStatus.CardInUse, mom), change);
        Assert.Equal([mom], await SavedMembersAsync(rewind, ct));
    }

    // A deactivated member keeps their card (DATA-4): they're reactivated, not added again.
    [Fact]
    public async Task AddAsync_CardOfAnInactiveMember_IsCardInUse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var mom = await AddMemberAsync(rewind, "Mom", Card, ct, isActive: false);

        var change = await Roster(rewind).AddAsync("Grandma", Card, ct);

        Assert.Equal(new MemberChange(MemberChangeStatus.CardInUse, mom), change);
    }

    // DATA-11: the card is free when AddAsync looks, then another member takes it before the save, and
    // the unique index refuses it. A trigger can't stand in for that other save: SQLite rolls a
    // trigger's insert back along with the insert that fails, so the card would look free again when
    // AddAsync looks after the refusal. The interceptor saves the other member for real, through a
    // context of its own, just before this save.
    [Fact]
    public async Task AddAsync_CardTakenDuringTheSave_IsCardInUse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var change = await Roster(rewind, new CardTakenJustBeforeEachSave(rewind)).AddAsync("Grandma", Card, ct);

        var mom = Assert.Single(await SavedMembersAsync(rewind, ct));
        Assert.Equal(new MemberChange(MemberChangeStatus.CardInUse, mom), change);
        Assert.Equal("Mom", mom.Name);
    }

    // NET-12, OBS-3: a card in use is a slip, found before the save, so the log has no error for it.
    // Left to the unique index, EF Core would log the refused save as one.
    [Fact]
    public async Task AddAsync_CardInUse_LogsNoError()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, "Mom", Card, ct);
        var log = new ListLoggerProvider();
        using var logged = new LoggedContexts(rewind, log);

        await Roster(rewind, logged).AddAsync("Grandma", Card, ct);

        Assert.Contains(log.Entries, entry => entry.Category.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)); // the log is EF Core's
        Assert.DoesNotContain(log.Entries, entry => entry.Level >= LogLevel.Error);
    }

    // OBS-3: a card turning up twice is worth finding in the log later, such as when a sheet of cards
    // was printed twice (ADR-0006).
    [Fact]
    public async Task AddAsync_CardInUse_LogsTheCard()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, "Mom", Card, ct);
        var log = new ListLogger<MemberRoster>();

        await new MemberRoster(rewind.DbFactory, log).AddAsync("Grandma", Card, ct);

        Assert.Equal([(LogLevel.Information, "Member card MEM-001 is Mom's already, so nothing was saved", (Exception?)null)], log.Entries);
    }

    // OBS-3: the database keeps each member only as they are now, so the log is where a change shows.
    [Fact]
    public async Task EachChange_AnyMember_IsLogged()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var log = new ListLogger<MemberRoster>();
        var roster = new MemberRoster(rewind.DbFactory, log);

        var added = await roster.AddAsync("Granma", Card, ct);
        await roster.RenameAsync(added.Member!.Id, "Grandma", ct);
        await roster.SetActiveAsync(added.Member.Id, isActive: false, ct);
        await roster.SetActiveAsync(added.Member.Id, isActive: true, ct);

        Assert.Equal(
            [
                (LogLevel.Information, "Added member MEM-001, Granma", (Exception?)null),
                (LogLevel.Information, "Renamed member MEM-001 from Granma to Grandma", null),
                (LogLevel.Information, "Member MEM-001, Grandma, is inactive", null),
                (LogLevel.Information, "Member MEM-001, Grandma, is active", null),
            ],
            log.Entries);
    }

    [Theory]
    [InlineData(null, Card, "name")]
    [InlineData("Grandma", null, "cardBarcode")]
    public async Task AddAsync_Null_IsRefused(string? name, string? cardBarcode, string paramName)
    {
        // NET-1: null on purpose, past the compiler's check, since this tests what AddAsync does with one.
        var add = () => WithNoDatabase().AddAsync(name!, cardBarcode!, TestContext.Current.CancellationToken);

        var refusal = await Assert.ThrowsAsync<ArgumentNullException>(add);
        Assert.Equal(paramName, refusal.ParamName);
    }

    [Fact]
    public async Task RenameAsync_NewName_SavesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var grandma = await AddMemberAsync(rewind, "Granma", Card, ct);

        var change = await Roster(rewind).RenameAsync(grandma.Id, "Grandma", ct);

        var renamed = grandma with { Name = "Grandma" };
        Assert.Equal(new MemberChange(MemberChangeStatus.Saved, renamed), change);
        Assert.Equal([renamed], await SavedMembersAsync(rewind, ct));
    }

    [Fact]
    public async Task RenameAsync_NameWithSpacesAround_SavesItTrimmed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var grandma = await AddMemberAsync(rewind, "Granma", Card, ct);

        await Roster(rewind).RenameAsync(grandma.Id, "  Grandma  ", ct);

        Assert.Equal("Grandma", Assert.Single(await SavedMembersAsync(rewind, ct)).Name);
    }

    [Fact]
    public async Task RenameAsync_BlankName_KeepsTheOldName()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var grandma = await AddMemberAsync(rewind, "Grandma", Card, ct);

        var change = await Roster(rewind).RenameAsync(grandma.Id, " ", ct);

        Assert.Equal(new MemberChange(MemberChangeStatus.NoName, null), change);
        Assert.Equal([grandma], await SavedMembersAsync(rewind, ct));
    }

    // OBS-3: the log is the only history a member has, so a rename that changes nothing leaves no line.
    [Fact]
    public async Task RenameAsync_SameNameOnceCleaned_LogsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var grandma = await AddMemberAsync(rewind, "Grandma", Card, ct);
        var log = new ListLogger<MemberRoster>();

        var change = await new MemberRoster(rewind.DbFactory, log).RenameAsync(grandma.Id, " Grandma ", ct);

        Assert.Equal(new MemberChange(MemberChangeStatus.Saved, grandma), change);
        Assert.Empty(log.Entries);
    }

    // Members are never deleted (DATA-4), so an Id the Store Manager doesn't have is a bug.
    [Fact]
    public async Task RenameAsync_UnknownMember_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var rename = () => Roster(rewind).RenameAsync(42, "Grandma", ct);

        await Assert.ThrowsAsync<InvalidOperationException>(rename);
    }

    [Fact]
    public async Task RenameAsync_NullName_IsRefused()
    {
        // NET-1: null on purpose, past the compiler's check, since this tests what RenameAsync does with one.
        var rename = () => WithNoDatabase().RenameAsync(1, null!, TestContext.Current.CancellationToken);

        var refusal = await Assert.ThrowsAsync<ArgumentNullException>(rename);
        Assert.Equal("name", refusal.ParamName);
    }

    // DATA-4: deactivating keeps the member, with their card.
    [Fact]
    public async Task SetActiveAsync_False_KeepsTheMemberInactive()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var dad = await AddMemberAsync(rewind, "Dad", Card, ct);

        var change = await Roster(rewind).SetActiveAsync(dad.Id, isActive: false, ct);

        var inactive = dad with { IsActive = false };
        Assert.Equal(new MemberChange(MemberChangeStatus.Saved, inactive), change);
        Assert.Equal([inactive], await SavedMembersAsync(rewind, ct));
    }

    [Fact]
    public async Task SetActiveAsync_TrueForAnInactiveMember_ReactivatesThem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var dad = await AddMemberAsync(rewind, "Dad", Card, ct, isActive: false);

        var change = await Roster(rewind).SetActiveAsync(dad.Id, isActive: true, ct);

        var active = dad with { IsActive = true };
        Assert.Equal(new MemberChange(MemberChangeStatus.Saved, active), change);
        Assert.Equal([active], await SavedMembersAsync(rewind, ct));
    }

    // As for a rename, a change that changes nothing leaves no line in the log.
    [Fact]
    public async Task SetActiveAsync_AlreadyInThatState_LogsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var dad = await AddMemberAsync(rewind, "Dad", Card, ct);
        var log = new ListLogger<MemberRoster>();

        var change = await new MemberRoster(rewind.DbFactory, log).SetActiveAsync(dad.Id, isActive: true, ct);

        Assert.Equal(new MemberChange(MemberChangeStatus.Saved, dad), change);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task SetActiveAsync_UnknownMember_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var deactivate = () => Roster(rewind).SetActiveAsync(42, isActive: false, ct);

        await Assert.ThrowsAsync<InvalidOperationException>(deactivate);
    }

    // SQLite sorts capitals before lower case unless told otherwise, which would put dave after Zoe.
    [Fact]
    public async Task ListAsync_SeveralMembers_ListsThemByNameIgnoringCapitals()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, "Zoe", "MEM-001", ct);
        await AddMemberAsync(rewind, "dave", "MEM-002", ct);
        await AddMemberAsync(rewind, "Carol", "MEM-003", ct);

        var members = await Roster(rewind).ListAsync(ct);

        Assert.Equal(["Carol", "dave", "Zoe"], members.Select(m => m.Name));
    }

    [Fact]
    public async Task ListAsync_TwoMembersWithOneName_ListsThemByCard()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddMemberAsync(rewind, "Sam", "MEM-002", ct);
        await AddMemberAsync(rewind, "Sam", "MEM-001", ct);

        var members = await Roster(rewind).ListAsync(ct);

        Assert.Equal(["MEM-001", "MEM-002"], members.Select(m => m.CardBarcode));
    }

    // The Store Manager lists inactive members too, so they can be reactivated.
    [Fact]
    public async Task ListAsync_InactiveMember_ListsThem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var dad = await AddMemberAsync(rewind, "Dad", Card, ct, isActive: false);

        var members = await Roster(rewind).ListAsync(ct);

        Assert.Equal([dad], members);
    }

    private static MemberRoster Roster(TempRewind rewind, IDbContextFactory<RewindDb>? dbFactory = null) =>
        new(dbFactory ?? rewind.DbFactory, NullLogger<MemberRoster>.Instance);

    // A null is refused before the database is opened.
    private static MemberRoster WithNoDatabase() => new(new NoDatabase(), NullLogger<MemberRoster>.Instance);

    // A member added before the test, saved straight to the database.
    private static async Task<MemberRow> AddMemberAsync(
        TempRewind rewind, string name, string card, CancellationToken cancellationToken, bool isActive = true)
    {
        await using var db = rewind.OpenDb();
        var member = new Member { Name = name, CardBarcode = card, IsActive = isActive };
        db.Members.Add(member);
        await db.SaveChangesAsync(cancellationToken);
        return new MemberRow(member.Id, name, card, isActive);
    }

    // The members as the database holds them, in the order they were added.
    private static async Task<List<MemberRow>> SavedMembersAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        return await db.Members
            .AsNoTracking()
            .OrderBy(m => m.Id)
            .Select(m => new MemberRow(m.Id, m.Name, m.CardBarcode, m.IsActive))
            .ToListAsync(cancellationToken);
    }

    private sealed class NoDatabase : IDbContextFactory<RewindDb>
    {
        public RewindDb CreateDbContext() => throw new InvalidOperationException("The roster opened the database.");
    }

    // Contexts each of whose saves another member beats to the card: just before the save, Mom is
    // saved with MEM-001 through a context of her own.
    private sealed class CardTakenJustBeforeEachSave(TempRewind rewind) : IDbContextFactory<RewindDb>
    {
        public RewindDb CreateDbContext() =>
            new(new DbContextOptionsBuilder<RewindDb>()
                .UseSqlite(RewindDb.ConnectionStringFor(rewind.Paths.Database))
                .AddInterceptors(new TakeTheCardFirst(rewind))
                .Options);
    }

    private sealed class TakeTheCardFirst(TempRewind rewind) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await AddMemberAsync(rewind, "Mom", Card, cancellationToken);
            return result;
        }
    }
}
