using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Rewind.Core.Catalog;
using Rewind.Core.Data;
using Rewind.Core.Members;
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
        var staffPicksId = await AddStaffPicksAsync(rewind, "Dad", "MEM-001", ct);
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
        await AddStaffPicksAsync(rewind, "Dad", "MEM-001", cancellationToken);
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

    // A member with their Staff Picks shelf, saved together straight to the database, as the Members tab adds them.
    private static async Task<int> AddStaffPicksAsync(TempRewind rewind, string name, string card, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        var shelf = new Shelf { Member = new Member { Name = name, CardBarcode = card } };
        db.Shelves.Add(shelf);
        await db.SaveChangesAsync(cancellationToken);
        return shelf.Id;
    }

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
}
