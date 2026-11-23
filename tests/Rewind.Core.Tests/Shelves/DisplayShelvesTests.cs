using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Rewind.Core.Catalog;
using Rewind.Core.Data;
using Rewind.Core.Members;
using Rewind.Core.Metadata;
using Rewind.Core.MovieNights;
using Rewind.Core.Shelves;

namespace Rewind.Core.Tests.Shelves;

public sealed class DisplayShelvesTests
{
    // October 1 to October 31. TempRewind's clock reads 10:15 PM on October 23 on the register, so it's in season.
    private static readonly Season Halloween = new(10, 1, 10, 31);

    // December 1 to Christmas Day, out of season on TempRewind's clock.
    private static readonly Season Christmas = new(12, 1, 12, 25);

    // October 24 to November 2, which starts the day after TempRewind's clock, so it's out of season too.
    private static readonly Season StartsTomorrow = new(10, 24, 11, 2);

    // 10:15 PM on Christmas Day on TempRewind's register, four hours behind UTC, so already December 26 in UTC.
    private static readonly DateTimeOffset ChristmasNight = new(2026, 12, 26, 2, 15, 0, TimeSpan.Zero);

    // From 10:15 PM to 12:30 AM, past midnight on the register's clock.
    private static readonly TimeSpan PastMidnight = TimeSpan.FromMinutes(135);

    [Fact]
    public async Task AddAsync_NameAndSeason_SavesAnActiveShelf()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var change = await Shelves(rewind).AddAsync("Halloween", 10, "1", 10, "31", ct);

        var saved = Assert.Single(await SavedShelvesAsync(rewind, ct));
        Assert.Equal(Seasonal(saved.Id, "Halloween", Halloween), saved);
        var added = new SeasonalShelfRow(saved.Id, "Halloween", Halloween, IsActive: true, InSeason: true);
        Assert.Equal(new SeasonalShelfChange(SeasonalShelfChangeStatus.Saved, added), change);
    }

    // The answer says whether the new shelf is in season tonight, as each of the list's rows does, so the tab needn't
    // read the list again after the save.
    [Fact]
    public async Task AddAsync_SeasonNotTonight_AnswersOutOfSeason()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var change = await Shelves(rewind).AddAsync("Christmas", 12, "1", 12, "25", ct);

        Assert.Equal(SeasonalShelfChangeStatus.Saved, change.Status);
        Assert.False(change.Shelf?.InSeason);
    }

    // SEC-5: cleaned as a member's name is, so the spaces around it go, and a line break in it becomes a space.
    [Fact]
    public async Task AddAsync_NameWithSpacesAndALineBreak_SavesItCleaned()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        await Shelves(rewind).AddAsync("  Christmas\nClassics  ", 12, "1", 12, "25", ct);

        Assert.Equal("Christmas Classics", Assert.Single(await SavedShelvesAsync(rewind, ct)).Name);
    }

    [Fact]
    public async Task AddAsync_NameLongerThanTheLimit_SavesItCut()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        await Shelves(rewind).AddAsync(new string('A', Shelf.MaxNameLength + 5), 10, "1", 10, "31", ct);

        Assert.Equal(new string('A', Shelf.MaxNameLength), Assert.Single(await SavedShelvesAsync(rewind, ct)).Name);
    }

    // Each day is read as every number typed into Rewind is, so spaces around it don't matter, and nor does a leading
    // zero.
    [Fact]
    public async Task AddAsync_DaysWithSpacesOrALeadingZero_ReadsThem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        await Shelves(rewind).AddAsync("Halloween", 10, " 1 ", 10, "031", ct);

        var saved = Assert.Single(await SavedShelvesAsync(rewind, ct));
        Assert.Equal(Seasonal(saved.Id, "Halloween", Halloween), saved);
    }

    // A month's last day is a day, at either end, and February 29 is one, since a season has no year (ADR-0015).
    [Theory]
    [InlineData(2, 29)]
    [InlineData(4, 30)]
    [InlineData(12, 31)]
    public async Task AddAsync_MonthsLastDay_IsADay(int month, int lastDay)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var day = lastDay.ToString(CultureInfo.InvariantCulture);

        var change = await Shelves(rewind).AddAsync("Last Day", month, day, month, day, ct);

        var saved = Assert.Single(await SavedShelvesAsync(rewind, ct));
        Assert.Equal(SeasonalShelfChangeStatus.Saved, change.Status);
        Assert.Equal(Seasonal(saved.Id, "Last Day", new Season(month, lastDay, month, lastDay)), saved);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddAsync_BlankName_IsNoName(string name)
    {
        var change = await WithNoDatabase().AddAsync(name, 10, "1", 10, "31", TestContext.Current.CancellationToken);

        Assert.Equal(new SeasonalShelfChange(SeasonalShelfChangeStatus.NoName, null), change);
    }

    // A day is digits alone, from 1 to its month's last. Anything else comes back as the day to fix, before the database
    // is asked: blank, 0, past the month's last day (32, November 31, February 30), words, a decimal point or a sign. 30
    // is a day of March and not of February, so the last two rows show each day is checked against its own month.
    [Theory]
    [InlineData(10, "", 10, "31", SeasonalShelfChangeStatus.NotAFromDay)]
    [InlineData(10, "   ", 10, "31", SeasonalShelfChangeStatus.NotAFromDay)]
    [InlineData(10, "0", 10, "31", SeasonalShelfChangeStatus.NotAFromDay)]
    [InlineData(10, "32", 10, "31", SeasonalShelfChangeStatus.NotAFromDay)]
    [InlineData(11, "31", 11, "30", SeasonalShelfChangeStatus.NotAFromDay)]
    [InlineData(10, "first", 10, "31", SeasonalShelfChangeStatus.NotAFromDay)]
    [InlineData(10, "1.5", 10, "31", SeasonalShelfChangeStatus.NotAFromDay)]
    [InlineData(10, "+1", 10, "31", SeasonalShelfChangeStatus.NotAFromDay)]
    [InlineData(10, "1", 10, "", SeasonalShelfChangeStatus.NotAToDay)]
    [InlineData(10, "1", 10, "0", SeasonalShelfChangeStatus.NotAToDay)]
    [InlineData(10, "1", 10, "32", SeasonalShelfChangeStatus.NotAToDay)]
    [InlineData(10, "1", 11, "31", SeasonalShelfChangeStatus.NotAToDay)]
    [InlineData(10, "1", 10, "-1", SeasonalShelfChangeStatus.NotAToDay)]
    [InlineData(2, "30", 3, "31", SeasonalShelfChangeStatus.NotAFromDay)]
    [InlineData(3, "1", 2, "30", SeasonalShelfChangeStatus.NotAToDay)]
    public async Task AddAsync_DayThatIsntOne_SaysWhichDayToFix(
        int fromMonth, string fromDay, int toMonth, string toDay, SeasonalShelfChangeStatus status)
    {
        var change = await WithNoDatabase().AddAsync("Halloween", fromMonth, fromDay, toMonth, toDay, TestContext.Current.CancellationToken);

        Assert.Equal(new SeasonalShelfChange(status, null), change);
    }

    // In the order the Store Manager fills them in: the name, then the first day, then the last.
    [Theory]
    [InlineData("", "0", "0", SeasonalShelfChangeStatus.NoName)]
    [InlineData("Halloween", "0", "0", SeasonalShelfChangeStatus.NotAFromDay)]
    public async Task AddAsync_SeveralThingsWrong_SaysTheFirst(string name, string fromDay, string toDay, SeasonalShelfChangeStatus status)
    {
        var change = await WithNoDatabase().AddAsync(name, 10, fromDay, 10, toDay, TestContext.Current.CancellationToken);

        Assert.Equal(status, change.Status);
    }

    // The months are picked from the twelve, so any other is a bug in the tab, refused by the month's own name.
    [Theory]
    [InlineData(0, 10, "fromMonth")]
    [InlineData(13, 10, "fromMonth")]
    [InlineData(10, 0, "toMonth")]
    [InlineData(10, 13, "toMonth")]
    public async Task AddAsync_MonthNotOneOfTheTwelve_IsRefusedByName(int fromMonth, int toMonth, string paramName)
    {
        var add = () => WithNoDatabase().AddAsync("Halloween", fromMonth, "1", toMonth, "1", TestContext.Current.CancellationToken);

        var refusal = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(add);
        Assert.Equal(paramName, refusal.ParamName);
    }

    // DATA-11: a name is one seasonal shelf's, active or not, whatever its letter case from A to Z, so the tab can say
    // to reactivate the shelf that has it. The answer is that shelf as it's saved, with its own season.
    [Theory]
    [InlineData("Halloween", true)]
    [InlineData("Halloween", false)]
    [InlineData("HALLOWEEN", true)]
    public async Task AddAsync_NameAnotherShelfHas_IsNameInUse(string ownerName, bool ownerIsActive)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var ownerId = await AddShelfAsync(rewind, ownerName, StartsTomorrow, ct, ownerIsActive);

        var change = await Shelves(rewind).AddAsync("Halloween", 10, "1", 10, "31", ct);

        var owner = new SeasonalShelfRow(ownerId, ownerName, StartsTomorrow, ownerIsActive, InSeason: false);
        Assert.Equal(new SeasonalShelfChange(SeasonalShelfChangeStatus.NameInUse, owner), change);
        Assert.Equal([Seasonal(ownerId, ownerName, StartsTomorrow, ownerIsActive)], await SavedShelvesAsync(rewind, ct));
    }

    // DATA-11: the name is free when AddAsync looks, then another shelf takes it before the save, and the names' index
    // refuses this one. The interceptor saves the other shelf for real, through a context of its own, just before this
    // save, as MemberRosterTests' lost card race does.
    [Fact]
    public async Task AddAsync_NameTakenDuringTheSave_IsNameInUse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        int? theirs = null;
        var theirsFirst = rewind.ContextsWith(new BeforeEachSave(async token => theirs ??= await AddShelfAsync(rewind, "HALLOWEEN", StartsTomorrow, token)));

        var change = await Shelves(rewind, theirsFirst).AddAsync("Halloween", 10, "1", 10, "31", ct);

        var owner = new SeasonalShelfRow(Assert.IsType<int>(theirs), "HALLOWEEN", StartsTomorrow, IsActive: true, InSeason: false);
        Assert.Equal(new SeasonalShelfChange(SeasonalShelfChangeStatus.NameInUse, owner), change);
        Assert.Equal([Seasonal(owner.Id, "HALLOWEEN", StartsTomorrow)], await SavedShelvesAsync(rewind, ct));
    }

    // NET-12, OBS-3: a name in use is a slip, found before the save, so the log has no error for it. Left to the names'
    // index, EF Core would log the refused save as one.
    [Fact]
    public async Task AddAsync_NameInUse_LogsNoError()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddShelfAsync(rewind, "Halloween", StartsTomorrow, ct);
        var log = new ListLoggerProvider();
        using var logged = new LoggedContexts(rewind, log);

        await Shelves(rewind, logged).AddAsync("Halloween", 10, "1", 10, "31", ct);

        Assert.Contains(log.Entries, entry => entry.Category.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)); // the log is EF Core's
        Assert.DoesNotContain(log.Entries, entry => entry.Level >= LogLevel.Error);
    }

    // OBS-3: unlike a member card printed twice, a name in use leaves nothing to find in the log later. Nothing changed,
    // and the tab says which shelf has the name.
    [Fact]
    public async Task AddAsync_NameInUse_LogsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddShelfAsync(rewind, "Halloween", StartsTomorrow, ct);
        var log = new ListLogger<DisplayShelves>();

        await Shelves(rewind, logger: log).AddAsync("Halloween", 10, "1", 10, "31", ct);

        Assert.Empty(log.Entries);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("fromDay")]
    [InlineData("toDay")]
    public async Task AddAsync_Null_IsRefusedByName(string parameter)
    {
        // NET-1: null on purpose, past the compiler's check, since this tests what AddAsync does with one.
        var add = () => WithNoDatabase().AddAsync(
            parameter == "name" ? null! : "Halloween",
            10,
            parameter == "fromDay" ? null! : "1",
            10,
            parameter == "toDay" ? null! : "31",
            TestContext.Current.CancellationToken);

        var refusal = await Assert.ThrowsAsync<ArgumentNullException>(add);
        Assert.Equal(parameter, refusal.ParamName);
    }

    // Renaming is for typos, so the season and whether the shelf is active stay.
    [Fact]
    public async Task RenameAsync_NewName_KeepsTheSeasonAndState()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var shelfId = await AddShelfAsync(rewind, "Halowen", Halloween, ct, isActive: false);

        var change = await Shelves(rewind).RenameAsync(shelfId, "Halloween", ct);

        var renamed = new SeasonalShelfRow(shelfId, "Halloween", Halloween, IsActive: false, InSeason: true);
        Assert.Equal(new SeasonalShelfChange(SeasonalShelfChangeStatus.Saved, renamed), change);
        Assert.Equal([Seasonal(shelfId, "Halloween", Halloween, isActive: false)], await SavedShelvesAsync(rewind, ct));
    }

    // The shelf's own name in another letter case is no other shelf's, so the fix is saved. The names' index takes it
    // too, since the only row it could hold the name against is this one.
    [Fact]
    public async Task RenameAsync_ItsOwnNameInAnotherCase_SavesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var shelfId = await AddShelfAsync(rewind, "halloween", Halloween, ct);

        var change = await Shelves(rewind).RenameAsync(shelfId, "Halloween", ct);

        Assert.Equal(SeasonalShelfChangeStatus.Saved, change.Status);
        Assert.Equal([Seasonal(shelfId, "Halloween", Halloween)], await SavedShelvesAsync(rewind, ct));
    }

    // DATA-11: as for a new shelf, a name another shelf has, active or not, in any letter case from A to Z.
    [Theory]
    [InlineData("Halloween", true)]
    [InlineData("Halloween", false)]
    [InlineData("HALLOWEEN", true)]
    public async Task RenameAsync_NameAnotherShelfHas_IsNameInUse(string ownerName, bool ownerIsActive)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var shelfId = await AddShelfAsync(rewind, "Spooky Season", Halloween, ct);
        var ownerId = await AddShelfAsync(rewind, ownerName, StartsTomorrow, ct, ownerIsActive);

        var change = await Shelves(rewind).RenameAsync(shelfId, "Halloween", ct);

        var owner = new SeasonalShelfRow(ownerId, ownerName, StartsTomorrow, ownerIsActive, InSeason: false);
        Assert.Equal(new SeasonalShelfChange(SeasonalShelfChangeStatus.NameInUse, owner), change);
        Assert.Equal(
            [Seasonal(shelfId, "Spooky Season", Halloween), Seasonal(ownerId, ownerName, StartsTomorrow, ownerIsActive)],
            await SavedShelvesAsync(rewind, ct));
    }

    // DATA-11: as for a new shelf, the name is free when RenameAsync looks, and another shelf takes it before the save.
    [Fact]
    public async Task RenameAsync_NameTakenDuringTheSave_IsNameInUse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var shelfId = await AddShelfAsync(rewind, "Spooky Season", Halloween, ct);
        int? theirs = null;
        var theirsFirst = rewind.ContextsWith(new BeforeEachSave(async token => theirs ??= await AddShelfAsync(rewind, "HALLOWEEN", StartsTomorrow, token)));

        var change = await Shelves(rewind, theirsFirst).RenameAsync(shelfId, "Halloween", ct);

        var owner = new SeasonalShelfRow(Assert.IsType<int>(theirs), "HALLOWEEN", StartsTomorrow, IsActive: true, InSeason: false);
        Assert.Equal(new SeasonalShelfChange(SeasonalShelfChangeStatus.NameInUse, owner), change);
        Assert.Equal(
            [Seasonal(shelfId, "Spooky Season", Halloween), Seasonal(owner.Id, "HALLOWEEN", StartsTomorrow)],
            await SavedShelvesAsync(rewind, ct));
    }

    // NET-12, OBS-3: as for a new shelf, found before the save, so EF Core logs no error.
    [Fact]
    public async Task RenameAsync_NameInUse_LogsNoError()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var shelfId = await AddShelfAsync(rewind, "Spooky Season", Halloween, ct);
        await AddShelfAsync(rewind, "Halloween", StartsTomorrow, ct);
        var log = new ListLoggerProvider();
        using var logged = new LoggedContexts(rewind, log);

        await Shelves(rewind, logged).RenameAsync(shelfId, "Halloween", ct);

        Assert.Contains(log.Entries, entry => entry.Category.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)); // the log is EF Core's
        Assert.DoesNotContain(log.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RenameAsync_BlankName_IsNoName(string name)
    {
        var change = await WithNoDatabase().RenameAsync(1, name, TestContext.Current.CancellationToken);

        Assert.Equal(new SeasonalShelfChange(SeasonalShelfChangeStatus.NoName, null), change);
    }

    [Fact]
    public async Task RenameAsync_NullName_IsRefused()
    {
        // NET-1: as above.
        var rename = () => WithNoDatabase().RenameAsync(1, null!, TestContext.Current.CancellationToken);

        var refusal = await Assert.ThrowsAsync<ArgumentNullException>(rename);
        Assert.Equal("name", refusal.ParamName);
    }

    // A new season keeps the name and whether the shelf is active, and the answer goes by it: October 24 to November 2
    // isn't in season on October 23.
    [Fact]
    public async Task SetSeasonAsync_NewSeason_KeepsTheNameAndState()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var shelfId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);

        var change = await Shelves(rewind).SetSeasonAsync(shelfId, 10, "24", 11, "2", ct);

        var redated = new SeasonalShelfRow(shelfId, "Halloween", StartsTomorrow, IsActive: true, InSeason: false);
        Assert.Equal(new SeasonalShelfChange(SeasonalShelfChangeStatus.Saved, redated), change);
        Assert.Equal([Seasonal(shelfId, "Halloween", StartsTomorrow)], await SavedShelvesAsync(rewind, ct));
    }

    // As for a new shelf: the first day, then the last, each against its own month, before the database is asked.
    [Theory]
    [InlineData(10, "", 10, "31", SeasonalShelfChangeStatus.NotAFromDay)]
    [InlineData(11, "31", 11, "30", SeasonalShelfChangeStatus.NotAFromDay)]
    [InlineData(10, "0", 10, "first", SeasonalShelfChangeStatus.NotAFromDay)]
    [InlineData(10, "1", 10, "32", SeasonalShelfChangeStatus.NotAToDay)]
    [InlineData(2, "30", 3, "31", SeasonalShelfChangeStatus.NotAFromDay)]
    [InlineData(3, "1", 2, "30", SeasonalShelfChangeStatus.NotAToDay)]
    public async Task SetSeasonAsync_DayThatIsntOne_SaysWhichDayToFix(
        int fromMonth, string fromDay, int toMonth, string toDay, SeasonalShelfChangeStatus status)
    {
        var change = await WithNoDatabase().SetSeasonAsync(1, fromMonth, fromDay, toMonth, toDay, TestContext.Current.CancellationToken);

        Assert.Equal(new SeasonalShelfChange(status, null), change);
    }

    [Theory]
    [InlineData(0, 10, "fromMonth")]
    [InlineData(13, 10, "fromMonth")]
    [InlineData(10, 0, "toMonth")]
    [InlineData(10, 13, "toMonth")]
    public async Task SetSeasonAsync_MonthNotOneOfTheTwelve_IsRefusedByName(int fromMonth, int toMonth, string paramName)
    {
        var redate = () => WithNoDatabase().SetSeasonAsync(1, fromMonth, "1", toMonth, "1", TestContext.Current.CancellationToken);

        var refusal = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(redate);
        Assert.Equal(paramName, refusal.ParamName);
    }

    [Theory]
    [InlineData("fromDay")]
    [InlineData("toDay")]
    public async Task SetSeasonAsync_Null_IsRefusedByName(string parameter)
    {
        // NET-1: as above.
        var redate = () => WithNoDatabase().SetSeasonAsync(
            1,
            10,
            parameter == "fromDay" ? null! : "1",
            10,
            parameter == "toDay" ? null! : "31",
            TestContext.Current.CancellationToken);

        var refusal = await Assert.ThrowsAsync<ArgumentNullException>(redate);
        Assert.Equal(parameter, refusal.ParamName);
    }

    // DATA-4: deactivating keeps the shelf, with its name and season.
    [Fact]
    public async Task SetActiveAsync_False_KeepsTheShelfInactive()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var shelfId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);

        var change = await Shelves(rewind).SetActiveAsync(shelfId, isActive: false, ct);

        var inactive = new SeasonalShelfRow(shelfId, "Halloween", Halloween, IsActive: false, InSeason: true);
        Assert.Equal(new SeasonalShelfChange(SeasonalShelfChangeStatus.Saved, inactive), change);
        Assert.Equal([Seasonal(shelfId, "Halloween", Halloween, isActive: false)], await SavedShelvesAsync(rewind, ct));
    }

    [Fact]
    public async Task SetActiveAsync_TrueForAnInactiveShelf_ReactivatesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var shelfId = await AddShelfAsync(rewind, "Halloween", Halloween, ct, isActive: false);

        var change = await Shelves(rewind).SetActiveAsync(shelfId, isActive: true, ct);

        var active = new SeasonalShelfRow(shelfId, "Halloween", Halloween, IsActive: true, InSeason: true);
        Assert.Equal(new SeasonalShelfChange(SeasonalShelfChangeStatus.Saved, active), change);
        Assert.Equal([Seasonal(shelfId, "Halloween", Halloween)], await SavedShelvesAsync(rewind, ct));
    }

    // ADR-0015: a deactivated shelf keeps its movies.
    [Fact]
    public async Task SetActiveAsync_False_KeepsItsMovies()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var shelfId = await AddShelfAsync(rewind, "Christmas", Christmas, ct);
        var movies = await PutTwoMoviesOnAsync(rewind, shelfId, ct);

        await Shelves(rewind).SetActiveAsync(shelfId, isActive: false, ct);

        Assert.Equal(movies, await MoviesOnAsync(rewind, shelfId, ct));
    }

    // ADR-0015: reactivated, the shelf is back with the movies it had.
    [Fact]
    public async Task SetActiveAsync_TrueOnceDeactivated_BringsItBackWithItsMovies()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var shelfId = await AddShelfAsync(rewind, "Christmas", Christmas, ct);
        var movies = await PutTwoMoviesOnAsync(rewind, shelfId, ct);
        var shelves = Shelves(rewind);
        await shelves.SetActiveAsync(shelfId, isActive: false, ct);

        var change = await shelves.SetActiveAsync(shelfId, isActive: true, ct);

        Assert.True(change.Shelf?.IsActive);
        Assert.Equal(movies, await MoviesOnAsync(rewind, shelfId, ct));
    }

    // A Staff Picks shelf goes by its member (ADR-0015), so its Id is none the Store Manager's list gives, and nor is one
    // no shelf has, since seasonal shelves are never deleted. Either is a bug, refused before anything changes, as
    // MemberRoster refuses a member who isn't there. The message is checked as well as the type: a Staff Picks shelf
    // read as a seasonal one would throw InvalidOperationException too, for the season it hasn't got.
    [Theory]
    [InlineData("rename", true)]
    [InlineData("season", true)]
    [InlineData("deactivate", true)]
    [InlineData("reactivate", true)]
    [InlineData("rename", false)]
    [InlineData("season", false)]
    [InlineData("deactivate", false)]
    [InlineData("reactivate", false)]
    public async Task EachChange_NotASeasonalShelfsId_IsABug(string change, bool staffPicks)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var staffPicksId = (await AddMemberAsync(rewind, "Dad", "MEM-001", ct)).ShelfId;
        var shelfId = staffPicks ? staffPicksId : 42;
        var before = await SavedShelvesAsync(rewind, ct);

        var act = () => ChangeAsync(Shelves(rewind), change, shelfId, ct);

        var bug = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.StartsWith($"No seasonal shelf has Id {shelfId}:", bug.Message, StringComparison.Ordinal);
        Assert.Equal(before, await SavedShelvesAsync(rewind, ct));
    }

    // OBS-3: the database keeps each shelf only as it is now, so the log is where a change shows: one line for each, with
    // its season in words, at Information, the level the register logs at unless Log:MinimumLevel says otherwise.
    [Fact]
    public async Task EachChange_AnyShelf_IsLoggedAtInformation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var log = new ListLoggerProvider();
        using var loggers = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Information).AddProvider(log));
        var shelves = Shelves(rewind, logger: loggers.CreateLogger<DisplayShelves>());

        var added = await shelves.AddAsync("Halowen", 10, "1", 10, "31", ct);
        Assert.NotNull(added.Shelf);
        await shelves.RenameAsync(added.Shelf.Id, "Halloween", ct);
        await shelves.SetSeasonAsync(added.Shelf.Id, 10, "1", 11, "2", ct);
        await shelves.SetActiveAsync(added.Shelf.Id, isActive: false, ct);
        await shelves.SetActiveAsync(added.Shelf.Id, isActive: true, ct);

        Assert.Equal(
            [
                (LogLevel.Information, "Added seasonal shelf Halowen, October 1 to October 31"),
                (LogLevel.Information, "Renamed seasonal shelf Halowen to Halloween"),
                (LogLevel.Information, "Seasonal shelf Halloween now runs October 1 to November 2, not October 1 to October 31"),
                (LogLevel.Information, "Seasonal shelf Halloween is inactive"),
                (LogLevel.Information, "Seasonal shelf Halloween is active"),
            ],
            log.Entries.Select(entry => (entry.Level, entry.Message)));
    }

    // The log reads alike on any PC, so a season's months are in English on one set to French too.
    [Fact]
    public async Task AddAsync_PcInAnotherLanguage_LogsTheSeasonInEnglish()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var log = new ListLogger<DisplayShelves>();
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            await Shelves(rewind, logger: log).AddAsync("Halloween", 10, "1", 10, "31", ct);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }

        Assert.Equal([(LogLevel.Information, "Added seasonal shelf Halloween, October 1 to October 31", (Exception?)null)], log.Entries);
    }

    // A change to what's saved already is no change: a name the same once cleaned, a season typed another way, and the
    // state the shelf is in. Nothing is saved, since any save here throws, and nothing is logged. Each answer is the
    // shelf as it is.
    [Fact]
    public async Task EachChange_SameAsSaved_SavesNothingAndLogsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var shelfId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        var noSaves = rewind.ContextsWith(new BeforeEachSave(_ => throw new InvalidOperationException("A change that changes nothing was saved.")));
        var log = new ListLogger<DisplayShelves>();
        var shelves = Shelves(rewind, noSaves, log);

        SeasonalShelfChange[] answers =
        [
            await shelves.RenameAsync(shelfId, " Halloween ", ct),
            await shelves.SetSeasonAsync(shelfId, 10, "01", 10, " 31", ct),
            await shelves.SetActiveAsync(shelfId, isActive: true, ct),
        ];

        var asItIs = new SeasonalShelfChange(
            SeasonalShelfChangeStatus.Saved, new SeasonalShelfRow(shelfId, "Halloween", Halloween, IsActive: true, InSeason: true));
        Assert.Equal([asItIs, asItIs, asItIs], answers);
        Assert.Empty(log.Entries);
    }

    // NET-7: tonight's day is read before the save, so the Store Manager leaving the tab just as a change lands can't
    // turn it into an exception: the change still comes back saved.
    [Theory]
    [InlineData("add")]
    [InlineData("rename")]
    [InlineData("season")]
    [InlineData("deactivate")]
    public async Task EachChange_CancelledOnceSaved_StillAnswersSaved(string change)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var shelfId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        using var leaving = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var leaveOnceSaved = rewind.ContextsWith(new AfterEachSave(_ => leaving.CancelAsync()));

        var answer = await ChangeAsync(Shelves(rewind, leaveOnceSaved), change, shelfId, leaving.Token);

        Assert.True(leaving.IsCancellationRequested);
        Assert.Equal(SeasonalShelfChangeStatus.Saved, answer.Status);
    }

    // NET-7: each change hands its token on to its first read, tonight's night, so a tab that closes stops it before it
    // sends the database anything. EF Core logs each command it sends, and logs none here.
    [Theory]
    [InlineData("add")]
    [InlineData("rename")]
    [InlineData("season")]
    [InlineData("deactivate")]
    public async Task EachChange_Cancelled_ThrowsBeforeAskingTheDatabase(string change)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var shelfId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        var log = new ListLoggerProvider();
        using var logged = new LoggedContexts(rewind, log);
        using var leaving = new CancellationTokenSource();
        await leaving.CancelAsync();

        var act = () => ChangeAsync(Shelves(rewind, logged), change, shelfId, leaving.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
        Assert.Contains(log.Entries, entry => entry.Category.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)); // the log is EF Core's
        Assert.DoesNotContain(log.Entries, entry => entry.Message.StartsWith("Executed DbCommand", StringComparison.Ordinal));
    }

    // NET-7: the read after tonight's night hears the token too. So a change that would save nothing, with a name another
    // shelf has, or the name, season or state the shelf has already, still stops when the tab closes once the night is
    // read, though no save is left to hear it.
    [Theory]
    [InlineData("add")]
    [InlineData("rename")]
    [InlineData("season")]
    [InlineData("reactivate")]
    public async Task EachChange_SavingNothingCancelledOnceTonightsNightIsRead_Throws(string change)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var shelfId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        using var leaving = new CancellationTokenSource();
        var shelves = Shelves(rewind, rewind.ContextsWith(new CancelAfterTheRead(leaving)));
        Func<Task> act = change switch
        {
            "add" => () => shelves.AddAsync("Halloween", 10, "1", 10, "31", leaving.Token),
            "rename" => () => shelves.RenameAsync(shelfId, "Halloween", leaving.Token),
            "season" => () => shelves.SetSeasonAsync(shelfId, 10, "1", 10, "31", leaving.Token),
            "reactivate" => () => shelves.SetActiveAsync(shelfId, isActive: true, leaving.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(change), change, "No such change."),
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
        Assert.True(leaving.IsCancellationRequested);
    }

    // NET-7: the save hands its token on too, so a tab that closes just then saves nothing.
    [Theory]
    [InlineData("add")]
    [InlineData("rename")]
    [InlineData("season")]
    [InlineData("deactivate")]
    public async Task EachChange_CancelledJustBeforeTheSave_SavesNothing(string change)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var shelfId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        using var leaving = new CancellationTokenSource();
        var cancelFirst = rewind.ContextsWith(new BeforeEachSave(_ => leaving.CancelAsync()));

        var act = () => ChangeAsync(Shelves(rewind, cancelFirst), change, shelfId, leaving.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
        Assert.True(leaving.IsCancellationRequested);
        Assert.Equal([Seasonal(shelfId, "Halloween", Halloween)], await SavedShelvesAsync(rewind, ct));
    }

    // NOCASE, so a name typed in lower case sorts among the others, not after every capital. A deactivated shelf is in
    // the list, and a member's Staff Picks shelf isn't.
    [Fact]
    public async Task ListSeasonalAsync_SeveralShelves_ListsThemByNameIgnoringCapitals()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var inOrder = await AddFourToSortAsync(rewind, ct);

        var shelves = await Shelves(rewind).ListSeasonalAsync(ct);

        Assert.Equal(inOrder, shelves.Select(shelf => shelf.Name));
    }

    // TEST-4: SQLite never promised the order it reads rows in, so these contexts read them backwards, which SQLite does
    // only for a read with no ORDER BY. So this shows the list doesn't rest on the order the shelves were read in. The
    // names' index leaves no two names to tie, so the Id after the name never decides, and no test can show it left out.
    [Fact]
    public async Task ListSeasonalAsync_ReadBackwards_ListsInTheSameOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var inOrder = await AddFourToSortAsync(rewind, ct);

        var shelves = await Shelves(rewind, rewind.ContextsWith(new BackwardsReads(), pooled: false)).ListSeasonalAsync(ct);

        Assert.Equal(inOrder, shelves.Select(shelf => shelf.Name));
    }

    // Each row has the shelf's season, whether it's active, and whether its season takes in tonight's day, active or not.
    [Fact]
    public async Task ListSeasonalAsync_EachShelf_HasItsSeasonStateAndWhetherItsInSeason()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var christmasId = await AddShelfAsync(rewind, "Christmas", Christmas, ct);
        var halloweenId = await AddShelfAsync(rewind, "Halloween", Halloween, ct, isActive: false);

        var shelves = await Shelves(rewind).ListSeasonalAsync(ct);

        Assert.Equal(
            [
                new SeasonalShelfRow(christmasId, "Christmas", Christmas, IsActive: true, InSeason: false),
                new SeasonalShelfRow(halloweenId, "Halloween", Halloween, IsActive: false, InSeason: true),
            ],
            shelves);
    }

    // ADR-0015: with no night open, the shelves go by the register's date. At 10:15 PM on October 23 there, it's already
    // October 24 in UTC, so the season that ends on the 23rd is in, and the one that starts on the 24th isn't.
    [Fact]
    public async Task ListSeasonalAsync_NoNightOpen_GoesByTheRegistersDate()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddShelfAsync(rewind, "Fall Break", new Season(10, 19, 10, 23), ct);
        await AddShelfAsync(rewind, "Halloween Week", new Season(10, 24, 10, 31), ct);

        var shelves = await Shelves(rewind).ListSeasonalAsync(ct);

        Assert.Equal([("Fall Break", true), ("Halloween Week", false)], shelves.Select(shelf => (shelf.Name, shelf.InSeason)));
    }

    // ADR-0015: a season goes by the movie night, so a night opened at 10:15 PM on Christmas Day keeps the Christmas
    // shelf in at 12:30 AM on December 26 on the register's clock.
    [Fact]
    public async Task ListSeasonalAsync_PastMidnightOnANightOpenedOnItsLastDay_IsStillInSeason()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        rewind.Time.SetUtcNow(ChristmasNight);
        await AddShelfAsync(rewind, "Christmas", Christmas, ct);
        await OpenANightAsync(rewind, ct);
        rewind.Time.Advance(PastMidnight);

        var christmas = Assert.Single(await Shelves(rewind).ListSeasonalAsync(ct));

        Assert.True(christmas.InSeason);
    }

    // At that same moment with no night open, it's December 26 on the register's clock, so the shelf is out.
    [Fact]
    public async Task ListSeasonalAsync_PastMidnightWithNoNightOpen_IsOutOfSeason()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        rewind.Time.SetUtcNow(ChristmasNight + PastMidnight);
        await AddShelfAsync(rewind, "Christmas", Christmas, ct);

        var christmas = Assert.Single(await Shelves(rewind).ListSeasonalAsync(ct));

        Assert.False(christmas.InSeason);
    }

    // ADR-0014's 16 hours, which BusinessDays keeps: a night open exactly that long still decides the shelves.
    [Fact]
    public async Task ListSeasonalAsync_NightOpenForExactlySixteenHours_StillGoesByIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        rewind.Time.SetUtcNow(ChristmasNight);
        await AddShelfAsync(rewind, "Christmas", Christmas, ct);
        await OpenANightAsync(rewind, ct);
        rewind.Time.Advance(TimeSpan.FromHours(16));

        var christmas = Assert.Single(await Shelves(rewind).ListSeasonalAsync(ct));

        Assert.True(christmas.InSeason);
    }

    // A second longer, it's a night nobody closed out, so the shelves go by the register's date, December 26.
    [Fact]
    public async Task ListSeasonalAsync_NightOpenForMoreThanSixteenHours_GoesByTheRegistersDate()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        rewind.Time.SetUtcNow(ChristmasNight);
        await AddShelfAsync(rewind, "Christmas", Christmas, ct);
        await OpenANightAsync(rewind, ct);
        rewind.Time.Advance(TimeSpan.FromHours(16) + TimeSpan.FromSeconds(1));

        var christmas = Assert.Single(await Shelves(rewind).ListSeasonalAsync(ct));

        Assert.False(christmas.InSeason);
    }

    // NET-8: the register's own calendar is its clock's time zone, not the PC's or UTC's. Eleven hours behind UTC, 10:15
    // PM on Christmas Day there is already December 26 in UTC, and on a PC two or more hours east of it, such as one on
    // US Eastern time, so only the register's calendar keeps the Christmas shelf in, whether by the night that opened
    // then or, with none, by the date.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ListSeasonalAsync_RegisterElevenHoursBehindUtc_GoesByItsOwnCalendar(bool nightOpen)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        rewind.Time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Far west", TimeSpan.FromHours(-11), "Far west", "Far west"));
        await rewind.StartAsync(ct);
        rewind.Time.SetUtcNow(new DateTimeOffset(2026, 12, 26, 9, 15, 0, TimeSpan.Zero));
        await AddShelfAsync(rewind, "Christmas", Christmas, ct);
        if (nightOpen)
        {
            await OpenANightAsync(rewind, ct);
            rewind.Time.Advance(PastMidnight);
        }

        var christmas = Assert.Single(await Shelves(rewind).ListSeasonalAsync(ct));

        Assert.True(christmas.InSeason);
    }

    // ADR-0015: the list goes by the night without starting one, so with none open it opens none. Only a member card at
    // Checkout does that.
    [Fact]
    public async Task ListSeasonalAsync_NoNightOpen_OpensNone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddShelfAsync(rewind, "Halloween", Halloween, ct);

        await Shelves(rewind).ListSeasonalAsync(ct);

        Assert.Empty(await SavedNightsAsync(rewind, ct));
    }

    // Nor does it end a night nobody closed out: the next night's first card does that (ADR-0014).
    [Fact]
    public async Task ListSeasonalAsync_NightOpenForMoreThanSixteenHours_LeavesItOpen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        var stale = await OpenANightAsync(rewind, ct);
        rewind.Time.Advance(TimeSpan.FromDays(7));

        await Shelves(rewind).ListSeasonalAsync(ct);

        Assert.Equal([stale], await SavedNightsAsync(rewind, ct));
    }

    // NET-7: the list hands its token on to its first read, tonight's night, so a tab that closes stops it before it sends
    // the database anything. EF Core logs each command it sends, and logs none here.
    [Fact]
    public async Task ListSeasonalAsync_Cancelled_ThrowsBeforeAskingTheDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        var log = new ListLoggerProvider();
        using var logged = new LoggedContexts(rewind, log);
        using var leaving = new CancellationTokenSource();
        await leaving.CancelAsync();

        var list = () => Shelves(rewind, logged).ListSeasonalAsync(leaving.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(list);
        Assert.Contains(log.Entries, entry => entry.Category.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)); // the log is EF Core's
        Assert.DoesNotContain(log.Entries, entry => entry.Message.StartsWith("Executed DbCommand", StringComparison.Ordinal));
    }

    // NET-7: the shelves' own read hears the token too, so a tab that closes once tonight's night is read still stops
    // the list.
    [Fact]
    public async Task ListSeasonalAsync_CancelledOnceTonightsNightIsRead_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        using var leaving = new CancellationTokenSource();
        var cancelAfterTheNight = rewind.ContextsWith(new CancelAfterTheRead(leaving));

        var list = () => Shelves(rewind, cancelAfterTheNight).ListSeasonalAsync(leaving.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(list);
        Assert.True(leaving.IsCancellationRequested);
    }

    // Build Plan 4.2's Done when, in Core: The Nightmare Before Christmas, put on the Halloween shelf and the Christmas
    // shelf, sits on both. Christmas is out of season on TempRewind's October 23, and takes the movie all the same. The
    // movie's shelves say so, and a search of each shelf finds it.
    [Fact]
    public async Task SetMovieShelvesAsync_DoneWhen_PutsTheNightmareBeforeChristmasOnBothShelves()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloweenId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        var christmasId = await AddShelfAsync(rewind, "Christmas", Christmas, ct);
        var nightmare = await AddMovieAsync(rewind, "The Nightmare Before Christmas", 1993, ct, inCase: "RV-00001");
        var shelves = Shelves(rewind);

        var answer = await shelves.SetMovieShelvesAsync(nightmare, [halloweenId, christmasId], ct);

        MovieShelfRow[] onBoth = [new(christmasId, "Christmas", Christmas, HasTheMovie: true), new(halloweenId, "Halloween", Halloween, HasTheMovie: true)];
        Assert.Equal(onBoth, answer);
        Assert.Equal(onBoth, await shelves.ListMovieShelvesAsync(nightmare, ct));
        Assert.Equal([halloweenId, christmasId], await ShelvesOfAsync(rewind, nightmare, ct));
        Assert.Equal([nightmare], await FoundOnAsync(rewind, halloweenId, ct));
        Assert.Equal([nightmare], await FoundOnAsync(rewind, christmasId, ct));
    }

    // Build Plan 4.2's Done when, on the night: on October 23, tonight's shelves hold Halloween and not Christmas, and
    // on December 10 the other way round. Each night opened at 10:15 PM on the register's clock, four hours behind UTC.
    [Theory]
    [InlineData(10, 23, "Halloween")]
    [InlineData(12, 10, "Christmas")]
    public async Task ListTonightsAsync_DoneWhen_HoldsTheShelfInSeasonAndNotTheOther(int month, int day, string inSeason)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        rewind.Time.SetUtcNow(new DateTimeOffset(2026, month, day, 22, 15, 0, TimeSpan.FromHours(-4)));
        await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        await AddShelfAsync(rewind, "Christmas", Christmas, ct);
        await OpenANightAsync(rewind, ct);

        var tonight = await Shelves(rewind).ListTonightsAsync(ct);

        Assert.Equal([inSeason], tonight.Select(shelf => shelf.Name));
    }

    // Only the seasonal shelves whose seasons take in tonight's day: on TempRewind's October 23, Halloween, and not
    // Christmas or the season that starts tomorrow. Each comes with its Id.
    [Fact]
    public async Task ListTonightsAsync_SeasonalShelves_ListsTheOnesInSeason()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddShelfAsync(rewind, "Christmas", Christmas, ct);
        var halloweenId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        await AddShelfAsync(rewind, "Day of the Dead", StartsTomorrow, ct);

        var tonight = await Shelves(rewind).ListTonightsAsync(ct);

        Assert.Equal([new ShelfRow(halloweenId, "Halloween")], tonight);
    }

    // ADR-0015: a deactivated shelf is hidden, though it's in season.
    [Fact]
    public async Task ListTonightsAsync_DeactivatedShelf_LeavesItOut()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddShelfAsync(rewind, "Halloween", Halloween, ct, isActive: false);
        var spookyId = await AddShelfAsync(rewind, "Spooky Season", Halloween, ct);

        var tonight = await Shelves(rewind).ListTonightsAsync(ct);

        Assert.Equal([new ShelfRow(spookyId, "Spooky Season")], tonight);
    }

    // ADR-0015: a member's shelf is hidden while they're deactivated.
    [Fact]
    public async Task ListTonightsAsync_DeactivatedMember_LeavesOutTheirStaffPicks()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var dad = await AddMemberAsync(rewind, "Dad", "MEM-001", ct);
        var mom = await AddMemberAsync(rewind, "Mom", "MEM-002", ct);
        await Roster(rewind).SetActiveAsync(mom.MemberId, isActive: false, ct);

        var tonight = await Shelves(rewind).ListTonightsAsync(ct);

        Assert.Equal([new ShelfRow(dad.ShelfId, "Dad's Staff Picks")], tonight);
    }

    // A shelf is listed whether or not anything is on it: Dad hasn't picked anything yet, and nor is anything on the
    // Halloween shelf, while Mom has a pick.
    [Fact]
    public async Task ListTonightsAsync_MemberWithNoPicks_IsStillListed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloweenId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        var dad = await AddMemberAsync(rewind, "Dad", "MEM-001", ct);
        var mom = await AddMemberAsync(rewind, "Mom", "MEM-002", ct);
        await PutOnAsync(rewind, mom.ShelfId, await AddMovieAsync(rewind, "Hocus Pocus", 1993, ct), ct);

        var tonight = await Shelves(rewind).ListTonightsAsync(ct);

        Assert.Equal(
            [new ShelfRow(halloweenId, "Halloween"), new ShelfRow(dad.ShelfId, "Dad's Staff Picks"), new ShelfRow(mom.ShelfId, "Mom's Staff Picks")],
            tonight);
    }

    // ADR-0015: a member's shelf is named for them each time it's read, so a rename on F5 follows.
    [Fact]
    public async Task ListTonightsAsync_MemberRenamed_NamesTheirShelfForTheNewName()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var grandpa = await AddMemberAsync(rewind, "Grampa", "MEM-001", ct);
        await Roster(rewind).RenameAsync(grandpa.MemberId, "Grandpa Joe", ct);

        var tonight = await Shelves(rewind).ListTonightsAsync(ct);

        Assert.Equal([new ShelfRow(grandpa.ShelfId, "Grandpa Joe's Staff Picks")], tonight);
    }

    // Seasonal shelves first, by name, then Staff Picks, by the member's name, then card, as the Members tab lists the
    // members. Both fold letter case from A to Z, so a name typed in lower case sorts among the others.
    [Fact]
    public async Task ListTonightsAsync_SeveralShelves_ListsSeasonalByNameThenStaffPicksByMember()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var inOrder = await AddSevenToSortAsync(rewind, ct);

        var tonight = await Shelves(rewind).ListTonightsAsync(ct);

        Assert.Equal(inOrder, tonight);
    }

    // TEST-4: SQLite never promised the order it reads rows in, so these contexts read them backwards wherever a read
    // has no ORDER BY. Neither the order the shelves were added in nor its reverse is the list's, so this fails if
    // either read loses its sort. SQLite's ORDER BY gives tied rows in the order it reads them, Id order, either way,
    // so no test can show the Id after a seasonal shelf's name left out: the names' index leaves no two names to tie.
    [Fact]
    public async Task ListTonightsAsync_ReadBackwards_ListsInTheSameOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var inOrder = await AddSevenToSortAsync(rewind, ct);

        var tonight = await Shelves(rewind, rewind.ContextsWith(new BackwardsReads(), pooled: false)).ListTonightsAsync(ct);

        Assert.Equal(inOrder, tonight);
    }

    // ADR-0015: tonight's day is the movie night's, as for the Store Manager's list, so a night opened at 10:15 PM on
    // Christmas Day still lists the Christmas shelf at 12:30 AM on December 26 on the register's clock.
    [Fact]
    public async Task ListTonightsAsync_PastMidnightOnANightOpenedOnItsLastDay_StillListsTheShelf()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        rewind.Time.SetUtcNow(ChristmasNight);
        var christmasId = await AddShelfAsync(rewind, "Christmas", Christmas, ct);
        await OpenANightAsync(rewind, ct);
        rewind.Time.Advance(PastMidnight);

        var tonight = await Shelves(rewind).ListTonightsAsync(ct);

        Assert.Equal([new ShelfRow(christmasId, "Christmas")], tonight);
    }

    // Every active seasonal shelf, in season or not, so a movie can go on Christmas in October, with its season, then
    // each active member's Staff Picks, which has none. A deactivated shelf isn't there, though it's in season, and nor
    // is a deactivated member's (ADR-0015).
    [Fact]
    public async Task ListMovieShelvesAsync_SeveralShelves_GivesEveryActiveOneInSeasonOrNot()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloweenId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        var christmasId = await AddShelfAsync(rewind, "Christmas", Christmas, ct);
        await AddShelfAsync(rewind, "Spooky Season", Halloween, ct, isActive: false);
        var dad = await AddMemberAsync(rewind, "Dad", "MEM-001", ct);
        var mom = await AddMemberAsync(rewind, "Mom", "MEM-002", ct);
        await Roster(rewind).SetActiveAsync(mom.MemberId, isActive: false, ct);
        var movie = await AddMovieAsync(rewind, "The Nightmare Before Christmas", 1993, ct);

        var shelves = await Shelves(rewind).ListMovieShelvesAsync(movie, ct);

        Assert.Equal(
            [
                new MovieShelfRow(christmasId, "Christmas", Christmas, HasTheMovie: false),
                new MovieShelfRow(halloweenId, "Halloween", Halloween, HasTheMovie: false),
                new MovieShelfRow(dad.ShelfId, "Dad's Staff Picks", Season: null, HasTheMovie: false),
            ],
            shelves);
    }

    // Each shelf says whether this movie sits on it. Elf sits on the Christmas shelf and Dad's, which this movie
    // doesn't, so a read that asked about any movie would tick those too.
    [Fact]
    public async Task ListMovieShelvesAsync_MovieOnSomeShelves_SaysWhichItSitsOn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloweenId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        var christmasId = await AddShelfAsync(rewind, "Christmas", Christmas, ct);
        var dad = await AddMemberAsync(rewind, "Dad", "MEM-001", ct);
        var mom = await AddMemberAsync(rewind, "Mom", "MEM-002", ct);
        var nightmare = await AddMovieAsync(rewind, "The Nightmare Before Christmas", 1993, ct);
        var elf = await AddMovieAsync(rewind, "Elf", 2003, ct);
        await PutOnAsync(rewind, halloweenId, nightmare, ct);
        await PutOnAsync(rewind, mom.ShelfId, nightmare, ct);
        await PutOnAsync(rewind, christmasId, elf, ct);
        await PutOnAsync(rewind, dad.ShelfId, elf, ct);

        var shelves = await Shelves(rewind).ListMovieShelvesAsync(nightmare, ct);

        Assert.Equal([false, true, false, true], shelves.Select(shelf => shelf.HasTheMovie));
    }

    // In tonight's order, as the form will show them: seasonal shelves by name, then Staff Picks by member, then card.
    [Fact]
    public async Task ListMovieShelvesAsync_SeveralShelves_ListsThemInTonightsOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var inOrder = await AddSevenToSortAsync(rewind, ct);
        var movie = await AddMovieAsync(rewind, "Hocus Pocus", 1993, ct);

        var shelves = await Shelves(rewind).ListMovieShelvesAsync(movie, ct);

        Assert.Equal(inOrder, shelves.Select(shelf => new ShelfRow(shelf.Id, shelf.Name)));
    }

    // On to one shelf and off another in one save (DATA-7), and the answer is the movie's shelves as they are now. It
    // sits on Halloween and Dad's, and goes on Christmas, off Halloween, and stays on Dad's.
    [Fact]
    public async Task SetMovieShelvesAsync_OnToOneShelfAndOffAnother_SavesBothInOneSave()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloweenId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        var christmasId = await AddShelfAsync(rewind, "Christmas", Christmas, ct);
        var dad = await AddMemberAsync(rewind, "Dad", "MEM-001", ct);
        var movie = await AddMovieAsync(rewind, "The Nightmare Before Christmas", 1993, ct);
        await PutOnAsync(rewind, halloweenId, movie, ct);
        await PutOnAsync(rewind, dad.ShelfId, movie, ct);
        var saves = 0;
        var counted = rewind.ContextsWith(new BeforeEachSave(_ =>
        {
            saves++;
            return Task.CompletedTask;
        }));

        var answer = await Shelves(rewind, counted).SetMovieShelvesAsync(movie, [christmasId, dad.ShelfId], ct);

        Assert.Equal(1, saves);
        Assert.Equal([christmasId, dad.ShelfId], await ShelvesOfAsync(rewind, movie, ct));
        Assert.Equal(
            [
                new MovieShelfRow(christmasId, "Christmas", Christmas, HasTheMovie: true),
                new MovieShelfRow(halloweenId, "Halloween", Halloween, HasTheMovie: false),
                new MovieShelfRow(dad.ShelfId, "Dad's Staff Picks", Season: null, HasTheMovie: true),
            ],
            answer);
    }

    // The shelves it sits on already, in another order, change nothing. Nothing is saved, since any save here throws,
    // and nothing is logged. The answer is the movie's shelves as they are.
    [Fact]
    public async Task SetMovieShelvesAsync_SameShelvesAgain_SavesNothingAndLogsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloweenId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        var christmasId = await AddShelfAsync(rewind, "Christmas", Christmas, ct);
        var dad = await AddMemberAsync(rewind, "Dad", "MEM-001", ct);
        var movie = await AddMovieAsync(rewind, "The Nightmare Before Christmas", 1993, ct);
        await PutOnAsync(rewind, halloweenId, movie, ct);
        await PutOnAsync(rewind, dad.ShelfId, movie, ct);
        var noSaves = rewind.ContextsWith(new BeforeEachSave(_ => throw new InvalidOperationException("A change that changes nothing was saved.")));
        var log = new ListLogger<DisplayShelves>();

        var answer = await Shelves(rewind, noSaves, log).SetMovieShelvesAsync(movie, [dad.ShelfId, halloweenId], ct);

        Assert.Equal(
            [
                new MovieShelfRow(christmasId, "Christmas", Christmas, HasTheMovie: false),
                new MovieShelfRow(halloweenId, "Halloween", Halloween, HasTheMovie: true),
                new MovieShelfRow(dad.ShelfId, "Dad's Staff Picks", Season: null, HasTheMovie: true),
            ],
            answer);
        Assert.Empty(log.Entries);
    }

    // An Id given twice is one shelf, as SetMovieShelvesAsync's summary says, so the movie goes on it once.
    [Fact]
    public async Task SetMovieShelvesAsync_IdGivenTwice_PutsTheMovieOnTheShelfOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloweenId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        var movie = await AddMovieAsync(rewind, "Hocus Pocus", 1993, ct);

        await Shelves(rewind).SetMovieShelvesAsync(movie, [halloweenId, halloweenId], ct);

        Assert.Equal([halloweenId], await ShelvesOfAsync(rewind, movie, ct));
    }

    // ADR-0015: a shelf the form doesn't show keeps what's on it. The movie sits on Halloween and on Mom's shelf, and
    // since then Halloween and Mom have both been deactivated. Neither shelf's Id is given, and neither loses the movie.
    [Fact]
    public async Task SetMovieShelvesAsync_DeactivatedShelfAndMember_KeepTheMovie()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var (halloweenId, christmasId, mom, movie) = await PutOnTwoThenDeactivateBothAsync(rewind, ct);

        await Shelves(rewind).SetMovieShelvesAsync(movie, [christmasId], ct);

        Assert.Equal([halloweenId, christmasId, mom.ShelfId], await ShelvesOfAsync(rewind, movie, ct));
    }

    // ADR-0015: reactivated, the shelf and the member's shelf are each back with the movie on them, as the form shows
    // them.
    [Fact]
    public async Task SetMovieShelvesAsync_DeactivatedShelfAndMemberReactivated_HaveTheMovieAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var (halloweenId, christmasId, mom, movie) = await PutOnTwoThenDeactivateBothAsync(rewind, ct);
        var shelves = Shelves(rewind);
        await shelves.SetMovieShelvesAsync(movie, [christmasId], ct);
        await shelves.SetActiveAsync(halloweenId, isActive: true, ct);
        await Roster(rewind).SetActiveAsync(mom.MemberId, isActive: true, ct);

        var shelvesNow = await shelves.ListMovieShelvesAsync(movie, ct);

        Assert.Equal(
            [
                new MovieShelfRow(christmasId, "Christmas", Christmas, HasTheMovie: true),
                new MovieShelfRow(halloweenId, "Halloween", Halloween, HasTheMovie: true),
                new MovieShelfRow(mom.ShelfId, "Mom's Staff Picks", Season: null, HasTheMovie: true),
            ],
            shelvesNow);
    }

    // OBS-3: what was on a shelf is only in the log (ADR-0015), so each shelf the movie goes on or comes off is a line,
    // in the form's order, at Information, the level the register logs at unless Log:MinimumLevel says otherwise. The
    // movie has its year, since remakes share titles, and a member's shelf is named as the screens name it.
    [Fact]
    public async Task SetMovieShelvesAsync_EachShelfChanged_IsLoggedAtInformation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloweenId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        var christmasId = await AddShelfAsync(rewind, "Christmas", Christmas, ct);
        var dad = await AddMemberAsync(rewind, "Dad", "MEM-001", ct);
        var movie = await AddMovieAsync(rewind, "The Nightmare Before Christmas", 1993, ct);
        await PutOnAsync(rewind, halloweenId, movie, ct);
        var log = new ListLoggerProvider();
        using var loggers = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Information).AddProvider(log));

        await Shelves(rewind, logger: loggers.CreateLogger<DisplayShelves>()).SetMovieShelvesAsync(movie, [christmasId, dad.ShelfId], ct);

        Assert.Equal(
            [
                (LogLevel.Information, "Put The Nightmare Before Christmas (1993) on shelf Christmas"),
                (LogLevel.Information, "Took The Nightmare Before Christmas (1993) off shelf Halloween"),
                (LogLevel.Information, "Put The Nightmare Before Christmas (1993) on shelf Dad's Staff Picks"),
            ],
            log.Entries.Select(entry => (entry.Level, entry.Message)));
    }

    // The form shows only the shelves ListMovieShelvesAsync gives, so any other Id is a bug, refused before anything
    // changes: one no shelf has, a deactivated shelf's and a deactivated member's. Left to the save, the movie would
    // come off Christmas. The message is checked as well as the type, as for the Store Manager's changes.
    [Theory]
    [InlineData("no shelf")]
    [InlineData("deactivated shelf")]
    [InlineData("deactivated member")]
    public async Task SetMovieShelvesAsync_ShelfTheFormDoesNotShow_IsABug(string shelf)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloweenId = await AddShelfAsync(rewind, "Halloween", Halloween, ct, isActive: false);
        var christmasId = await AddShelfAsync(rewind, "Christmas", Christmas, ct);
        var mom = await AddMemberAsync(rewind, "Mom", "MEM-001", ct);
        await Roster(rewind).SetActiveAsync(mom.MemberId, isActive: false, ct);
        var movie = await AddMovieAsync(rewind, "The Nightmare Before Christmas", 1993, ct);
        await PutOnAsync(rewind, christmasId, movie, ct);
        var shelfId = shelf switch
        {
            "no shelf" => 42,
            "deactivated shelf" => halloweenId,
            "deactivated member" => mom.ShelfId,
            _ => throw new ArgumentOutOfRangeException(nameof(shelf), shelf, "No such shelf."),
        };

        var act = () => Shelves(rewind).SetMovieShelvesAsync(movie, [shelfId], ct);

        var bug = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.StartsWith($"No shelf a movie can go on has Id {shelfId}:", bug.Message, StringComparison.Ordinal);
        Assert.Equal([christmasId], await ShelvesOfAsync(rewind, movie, ct));
    }

    // Rewind never deletes a movie, so an Id no movie has is a bug, for a movie's shelves and for putting it on them,
    // as MemberRoster refuses a member who isn't there, and nothing changes. The message is checked as well as the
    // type: left to the save, the shelves' foreign key would refuse the row with an exception of its own.
    [Theory]
    [InlineData("list")]
    [InlineData("set")]
    public async Task EachMovieCall_MovieThatIsntThere_IsABug(string call)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloweenId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        var shelves = Shelves(rewind);
        Func<Task> act = call switch
        {
            "list" => () => shelves.ListMovieShelvesAsync(42, ct),
            "set" => () => shelves.SetMovieShelvesAsync(42, [halloweenId], ct),
            _ => throw new ArgumentOutOfRangeException(nameof(call), call, "No such call."),
        };

        var bug = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.StartsWith("No movie has Id 42,", bug.Message, StringComparison.Ordinal);
        Assert.Empty(await MoviesOnAsync(rewind, halloweenId, ct));
    }

    [Fact]
    public async Task SetMovieShelvesAsync_NullShelves_IsRefused()
    {
        // NET-1: null on purpose, past the compiler's check, since this tests what SetMovieShelvesAsync does with one.
        var act = () => WithNoDatabase().SetMovieShelvesAsync(1, null!, TestContext.Current.CancellationToken);

        var refusal = await Assert.ThrowsAsync<ArgumentNullException>(act);
        Assert.Equal("shelfIds", refusal.ParamName);
    }

    // The look before the save leaves the shelves' key to refuse a row only when another save puts the movie on the
    // same shelf in between. The form sends one save at a time, so that's a bug, and the refusal goes up as it is
    // (NET-12), taking the rest of the save with it (DATA-7): the movie was to come off Christmas, and is still on it.
    [Fact]
    public async Task SetMovieShelvesAsync_PutOnTheShelfByAnotherSaveFirst_ThrowsAndSavesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloweenId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        var christmasId = await AddShelfAsync(rewind, "Christmas", Christmas, ct);
        var movie = await AddMovieAsync(rewind, "The Nightmare Before Christmas", 1993, ct);
        await PutOnAsync(rewind, christmasId, movie, ct);
        var theirsFirst = rewind.ContextsWith(new BeforeEachSave(token => PutOnAsync(rewind, halloweenId, movie, token)));

        var act = () => Shelves(rewind, theirsFirst).SetMovieShelvesAsync(movie, [halloweenId], ct);

        await SqliteAssert.RefusesAsync(SqliteConstraint.PrimaryKey, act);
        Assert.Equal([halloweenId, christmasId], await ShelvesOfAsync(rewind, movie, ct));
    }

    // The same for a row to delete: another save takes the movie off Christmas in between, so this save's delete finds
    // no row, and EF Core throws, taking the rest of the save with it: the movie doesn't go on Halloween.
    [Fact]
    public async Task SetMovieShelvesAsync_TakenOffTheShelfByAnotherSaveFirst_ThrowsAndSavesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloweenId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        var christmasId = await AddShelfAsync(rewind, "Christmas", Christmas, ct);
        var movie = await AddMovieAsync(rewind, "The Nightmare Before Christmas", 1993, ct);
        await PutOnAsync(rewind, christmasId, movie, ct);
        var theirsFirst = rewind.ContextsWith(new BeforeEachSave(token => TakeOffAsync(rewind, christmasId, movie, token)));

        var act = () => Shelves(rewind, theirsFirst).SetMovieShelvesAsync(movie, [halloweenId], ct);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(act);
        Assert.Empty(await ShelvesOfAsync(rewind, movie, ct));
    }

    // NET-7: the save hands its token on, so a form that closes just then leaves the shelves as they were. Nothing
    // changed, so nothing is logged either: each line is written once its save is in.
    [Fact]
    public async Task SetMovieShelvesAsync_CancelledJustBeforeTheSave_LeavesTheShelvesAsTheyWere()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloweenId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        var christmasId = await AddShelfAsync(rewind, "Christmas", Christmas, ct);
        var movie = await AddMovieAsync(rewind, "The Nightmare Before Christmas", 1993, ct);
        await PutOnAsync(rewind, halloweenId, movie, ct);
        using var leaving = new CancellationTokenSource();
        var cancelFirst = rewind.ContextsWith(new BeforeEachSave(_ => leaving.CancelAsync()));
        var log = new ListLogger<DisplayShelves>();

        var act = () => Shelves(rewind, cancelFirst, log).SetMovieShelvesAsync(movie, [christmasId], leaving.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
        Assert.True(leaving.IsCancellationRequested);
        Assert.Equal([halloweenId], await ShelvesOfAsync(rewind, movie, ct));
        Assert.Empty(log.Entries);
    }

    // NET-7: once the save is in, nothing waits on the token, so a form that closes just then still gets the answer.
    [Fact]
    public async Task SetMovieShelvesAsync_CancelledOnceSaved_StillAnswers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloweenId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        var movie = await AddMovieAsync(rewind, "Hocus Pocus", 1993, ct);
        using var leaving = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var leaveOnceSaved = rewind.ContextsWith(new AfterEachSave(_ => leaving.CancelAsync()));

        var answer = await Shelves(rewind, leaveOnceSaved).SetMovieShelvesAsync(movie, [halloweenId], leaving.Token);

        Assert.True(leaving.IsCancellationRequested);
        Assert.Equal([new MovieShelfRow(halloweenId, "Halloween", Halloween, HasTheMovie: true)], answer);
    }

    // NET-7: each of Ask the Clerk's calls hands its token on to its first read, tonight's night or the movie, so a
    // screen that closes stops it before it sends the database anything. EF Core logs each command it sends, and logs
    // none here.
    [Theory]
    [InlineData("tonight")]
    [InlineData("movie")]
    [InlineData("set")]
    public async Task EachAskTheClerkCall_Cancelled_ThrowsBeforeAskingTheDatabase(string call)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloweenId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        var movie = await AddMovieAsync(rewind, "Hocus Pocus", 1993, ct);
        await PutOnAsync(rewind, halloweenId, movie, ct);
        var log = new ListLoggerProvider();
        using var logged = new LoggedContexts(rewind, log);
        using var leaving = new CancellationTokenSource();
        await leaving.CancelAsync();

        var act = () => AskAsync(Shelves(rewind, logged), call, movie, halloweenId, leaving.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
        Assert.Contains(log.Entries, entry => entry.Category.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)); // the log is EF Core's
        Assert.DoesNotContain(log.Entries, entry => entry.Message.StartsWith("Executed DbCommand", StringComparison.Ordinal));
    }

    // NET-7: each of these calls reads three times: tonight's night or the movie, then the seasonal shelves, then the
    // Staff Picks. Each later read hears the token too, so a screen that closes once the first or the second read is in
    // still stops the call, though here, putting the movie on the shelf it sits on already, no save is left to hear it.
    [Theory]
    [InlineData("tonight", 1)]
    [InlineData("tonight", 2)]
    [InlineData("movie", 1)]
    [InlineData("movie", 2)]
    [InlineData("set", 1)]
    [InlineData("set", 2)]
    public async Task EachAskTheClerkCall_CancelledOnceAReadIsIn_Throws(string call, int reads)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var halloweenId = await AddShelfAsync(rewind, "Halloween", Halloween, ct);
        var movie = await AddMovieAsync(rewind, "Hocus Pocus", 1993, ct);
        await PutOnAsync(rewind, halloweenId, movie, ct);
        using var leaving = new CancellationTokenSource();
        var cancelAfterTheReads = rewind.ContextsWith(new CancelAfterTheRead(leaving, reads));

        var act = () => AskAsync(Shelves(rewind, cancelAfterTheReads), call, movie, halloweenId, leaving.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
        Assert.True(leaving.IsCancellationRequested);
    }

    // The business days read the same contexts as the shelves, so a test's interceptor, or NoDatabase, covers both.
    private static DisplayShelves Shelves(
        TempRewind rewind, IDbContextFactory<RewindDb>? dbFactory = null, ILogger<DisplayShelves>? logger = null)
    {
        var contexts = dbFactory ?? rewind.DbFactory;
        var days = new BusinessDays(contexts, rewind.Time, NullLogger<BusinessDays>.Instance);
        return new(contexts, days, rewind.Time, logger ?? NullLogger<DisplayShelves>.Instance);
    }

    // For what's answered before the database is opened, such as a blank name, a day that isn't one, or a null. The
    // business days can't open it either, so tonight's night isn't read.
    private static DisplayShelves WithNoDatabase()
    {
        var none = new NoDatabase("The display shelves");
        var time = new FakeTimeProvider();
        return new(none, new BusinessDays(none, time, NullLogger<BusinessDays>.Instance), time, NullLogger<DisplayShelves>.Instance);
    }

    // One change of each kind to a shelf saved as Halloween, October 1 to 31, and active, each of which changes it, but
    // for reactivating, which asks for the state it's in. Adding one adds Christmas.
    private static Task<SeasonalShelfChange> ChangeAsync(
        DisplayShelves shelves, string change, int shelfId, CancellationToken cancellationToken) =>
        change switch
        {
            "add" => shelves.AddAsync("Christmas", 12, "1", 12, "25", cancellationToken),
            "rename" => shelves.RenameAsync(shelfId, "Spooky Season", cancellationToken),
            "season" => shelves.SetSeasonAsync(shelfId, 10, "24", 11, "2", cancellationToken),
            "deactivate" => shelves.SetActiveAsync(shelfId, isActive: false, cancellationToken),
            "reactivate" => shelves.SetActiveAsync(shelfId, isActive: true, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(change), change, "No such change."),
        };

    // A member's Staff Picks shelf, which the list leaves out, then three seasonal shelves, added out of the list's order,
    // one of them deactivated and one typed in lower case. Returns their names in the list's order.
    private static async Task<string[]> AddFourToSortAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await AddMemberAsync(rewind, "Dad", "MEM-001", cancellationToken);
        await AddShelfAsync(rewind, "Halloween", Halloween, cancellationToken);
        await AddShelfAsync(rewind, "Summer", new Season(6, 21, 9, 22), cancellationToken, isActive: false);
        await AddShelfAsync(rewind, "christmas", Christmas, cancellationToken);
        return ["christmas", "Halloween", "Summer"];
    }

    // A seasonal shelf saved straight to the database, before the test or by another save during it.
    private static async Task<int> AddShelfAsync(
        TempRewind rewind, string name, Season season, CancellationToken cancellationToken, bool isActive = true)
    {
        await using var db = rewind.OpenDb();
        var shelf = new Shelf
        {
            Name = name,
            FromMonth = season.FromMonth,
            FromDay = season.FromDay,
            ToMonth = season.ToMonth,
            ToDay = season.ToDay,
            IsActive = isActive,
        };
        db.Shelves.Add(shelf);
        await db.SaveChangesAsync(cancellationToken);
        return shelf.Id;
    }

    // A member added as the Members tab adds them, through MemberRoster, which gives them their Staff Picks shelf in
    // the same save (ADR-0015). One saved straight to the database would have none.
    private static async Task<StaffPicks> AddMemberAsync(TempRewind rewind, string name, string card, CancellationToken cancellationToken)
    {
        var added = await Roster(rewind).AddAsync(name, card, cancellationToken);
        Assert.NotNull(added.Member);
        var memberId = added.Member.Id;
        await using var db = rewind.OpenDb();
        var shelfId = await db.Shelves.Where(s => s.MemberId == memberId).Select(s => s.Id).SingleAsync(cancellationToken);
        return new StaffPicks(memberId, shelfId);
    }

    private static MemberRoster Roster(TempRewind rewind) => new(rewind.DbFactory, NullLogger<MemberRoster>.Instance);

    // Three seasonal shelves, each in season on TempRewind's October 23, and four members, each with their Staff Picks.
    // Neither the order they're added in nor its reverse is the list's, and a name in lower case sorts one way when
    // letter case counts and another when it's folded: candy Corn before Halloween, and grandma before Mom. Two members
    // share a name, so their cards decide. Returns the shelves in the list's order.
    private static async Task<ShelfRow[]> AddSevenToSortAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        var halloweenId = await AddShelfAsync(rewind, "Halloween", Halloween, cancellationToken);
        var secondDad = await AddMemberAsync(rewind, "Dad", "MEM-002", cancellationToken);
        var candyCornId = await AddShelfAsync(rewind, "candy Corn", Halloween, cancellationToken);
        var firstDad = await AddMemberAsync(rewind, "Dad", "MEM-001", cancellationToken);
        var mom = await AddMemberAsync(rewind, "Mom", "MEM-003", cancellationToken);
        var spookyId = await AddShelfAsync(rewind, "Spooky Season", Halloween, cancellationToken);
        var grandma = await AddMemberAsync(rewind, "grandma", "MEM-004", cancellationToken);
        return
        [
            new(candyCornId, "candy Corn"),
            new(halloweenId, "Halloween"),
            new(spookyId, "Spooky Season"),
            new(firstDad.ShelfId, "Dad's Staff Picks"),
            new(secondDad.ShelfId, "Dad's Staff Picks"),
            new(grandma.ShelfId, "grandma's Staff Picks"),
            new(mom.ShelfId, "Mom's Staff Picks"),
        ];
    }

    // Halloween and Christmas, Mom with her Staff Picks, and a movie that sits on Halloween and on Mom's shelf. Then
    // Halloween is deactivated, by the Store Manager's call, and so is Mom, by MemberRoster's, so the form shows
    // neither shelf.
    private static async Task<(int HalloweenId, int ChristmasId, StaffPicks Mom, int Movie)> PutOnTwoThenDeactivateBothAsync(
        TempRewind rewind, CancellationToken cancellationToken)
    {
        var halloweenId = await AddShelfAsync(rewind, "Halloween", Halloween, cancellationToken);
        var christmasId = await AddShelfAsync(rewind, "Christmas", Christmas, cancellationToken);
        var mom = await AddMemberAsync(rewind, "Mom", "MEM-001", cancellationToken);
        var movie = await AddMovieAsync(rewind, "The Nightmare Before Christmas", 1993, cancellationToken);
        await PutOnAsync(rewind, halloweenId, movie, cancellationToken);
        await PutOnAsync(rewind, mom.ShelfId, movie, cancellationToken);
        await Shelves(rewind).SetActiveAsync(halloweenId, isActive: false, cancellationToken);
        await Roster(rewind).SetActiveAsync(mom.MemberId, isActive: false, cancellationToken);
        return (halloweenId, christmasId, mom, movie);
    }

    // A movie saved straight to the database, in no case, or in a case with the store label given, which a search needs
    // to find it. A movie needn't be in a case to go on a shelf. Returns its Id.
    private static async Task<int> AddMovieAsync(
        TempRewind rewind, string title, int year, CancellationToken cancellationToken, string? inCase = null)
    {
        await using var db = rewind.OpenDb();
        var movie = new Movie { Title = title, Year = year };
        db.Movies.Add(movie);
        if (inCase is not null)
        {
            db.Cases.Add(new Case { StoreLabel = inCase, Movies = { movie } });
        }

        await db.SaveChangesAsync(cancellationToken);
        return movie.Id;
    }

    // A movie put on a shelf straight in the database, as an earlier save of the form would have, or another save.
    private static async Task PutOnAsync(TempRewind rewind, int shelfId, int movieId, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        db.ShelfMovies.Add(new ShelfMovie { ShelfId = shelfId, MovieId = movieId });
        await db.SaveChangesAsync(cancellationToken);
    }

    // A movie taken off a shelf straight in the database, by another save.
    private static async Task TakeOffAsync(TempRewind rewind, int shelfId, int movieId, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        db.ShelfMovies.Remove(new ShelfMovie { ShelfId = shelfId, MovieId = movieId });
        await db.SaveChangesAsync(cancellationToken);
    }

    // The shelves a movie sits on, as the database holds them, in the order the shelves were added.
    private static async Task<List<int>> ShelvesOfAsync(TempRewind rewind, int movieId, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        return await db.ShelfMovies
            .AsNoTracking()
            .Where(sm => sm.MovieId == movieId)
            .OrderBy(sm => sm.ShelfId)
            .Select(sm => sm.ShelfId)
            .ToListAsync(cancellationToken);
    }

    // The movies a search of the shelf finds, by Id, as Ask the Clerk's search finds them.
    private static async Task<List<int>> FoundOnAsync(TempRewind rewind, int shelfId, CancellationToken cancellationToken)
    {
        var search = new CatalogSearch(rewind.DbFactory, new Posters(Options.Create(rewind.Paths), NullLogger<Posters>.Instance), rewind.Time);
        var found = await search.SearchAsync(null, null, null, null, shelfId, cancellationToken);
        return [.. found.Hits.Select(hit => hit.MovieId)];
    }

    // One of Ask the Clerk's calls, about a movie that sits on the Halloween shelf. Putting it there asks for the shelf
    // it sits on already, so it saves nothing.
    private static Task AskAsync(DisplayShelves shelves, string call, int movieId, int halloweenId, CancellationToken cancellationToken) =>
        call switch
        {
            "tonight" => shelves.ListTonightsAsync(cancellationToken),
            "movie" => shelves.ListMovieShelvesAsync(movieId, cancellationToken),
            "set" => shelves.SetMovieShelvesAsync(movieId, [halloweenId], cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(call), call, "No such call."),
        };

    // Two movies in no case, put straight on the shelf. Returns their Ids in the order they were added.
    private static async Task<List<int>> PutTwoMoviesOnAsync(TempRewind rewind, int shelfId, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        List<Movie> movies = [new() { Title = "The Nightmare Before Christmas" }, new() { Title = "Elf" }];
        db.Movies.AddRange(movies);
        await db.SaveChangesAsync(cancellationToken);
        db.ShelfMovies.AddRange(movies.Select(movie => new ShelfMovie { ShelfId = shelfId, MovieId = movie.Id }));
        await db.SaveChangesAsync(cancellationToken);
        return [.. movies.Select(movie => movie.Id)];
    }

    // The movies on a shelf, in the order they were added.
    private static async Task<List<int>> MoviesOnAsync(TempRewind rewind, int shelfId, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        return await db.ShelfMovies
            .AsNoTracking()
            .Where(sm => sm.ShelfId == shelfId)
            .OrderBy(sm => sm.MovieId)
            .Select(sm => sm.MovieId)
            .ToListAsync(cancellationToken);
    }

    // A night opened before the test at the clock's time, saved straight to the database.
    private static async Task<BusinessDayRow> OpenANightAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        var night = new BusinessDay { OpenedAt = rewind.Time.GetUtcNow().UtcDateTime };
        db.BusinessDays.Add(night);
        await db.SaveChangesAsync(cancellationToken);
        return new BusinessDayRow(night.Id, night.OpenedAt, night.ClosedAt);
    }

    // The nights as the database holds them, oldest first.
    private static async Task<List<BusinessDayRow>> SavedNightsAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        return await db.BusinessDays
            .AsNoTracking()
            .OrderBy(d => d.Id)
            .Select(d => new BusinessDayRow(d.Id, d.OpenedAt, d.ClosedAt))
            .ToListAsync(cancellationToken);
    }

    // The shelves as the database holds them, both kinds, in the order they were added.
    private static async Task<List<SavedShelf>> SavedShelvesAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        return await db.Shelves
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .Select(s => new SavedShelf(s.Id, s.MemberId, s.Name, s.FromMonth, s.FromDay, s.ToMonth, s.ToDay, s.IsActive))
            .ToListAsync(cancellationToken);
    }

    // A seasonal shelf as SavedShelvesAsync reads it.
    private static SavedShelf Seasonal(int id, string name, Season season, bool isActive = true) =>
        new(id, null, name, season.FromMonth, season.FromDay, season.ToMonth, season.ToDay, isActive);

    private sealed record SavedShelf(
        int Id, int? MemberId, string? Name, int? FromMonth, int? FromDay, int? ToMonth, int? ToDay, bool IsActive);

    // A member and their Staff Picks shelf, by Id.
    private sealed record StaffPicks(int MemberId, int ShelfId);
}
