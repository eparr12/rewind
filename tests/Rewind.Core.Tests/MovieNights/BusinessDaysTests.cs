using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Rewind.Core.Data;
using Rewind.Core.MovieNights;

namespace Rewind.Core.Tests.MovieNights;

public sealed class BusinessDaysTests
{
    [Fact]
    public async Task OpenOrCurrentAsync_NoneOpen_OpensOneAtTheClocksTime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var night = await Days(rewind).OpenOrCurrentAsync(ct);

        var saved = Assert.Single(await SavedDaysAsync(rewind, ct));
        Assert.Equal(saved, night);
        Assert.Equal((Now(rewind), (DateTime?)null), (saved.OpenedAt, saved.ClosedAt));
    }

    [Fact]
    public async Task OpenOrCurrentAsync_OneOpen_ReturnsThatOne()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var days = Days(rewind);
        var opened = await days.OpenOrCurrentAsync(ct);
        rewind.Time.Advance(TimeSpan.FromMinutes(5));

        var current = await days.OpenOrCurrentAsync(ct);

        Assert.Equal(opened, current);
        Assert.Equal([opened], await SavedDaysAsync(rewind, ct));
    }

    // DATA-8: a night is a row here, not a calendar date. This one runs from 7:30 PM to 12:30 AM on the
    // register's clock, four hours behind UTC, so it passes midnight in both.
    [Fact]
    public async Task OpenOrCurrentAsync_PastMidnight_ReturnsTheSameNight()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        rewind.Time.SetUtcNow(new DateTimeOffset(2026, 10, 24, 23, 30, 0, TimeSpan.Zero));
        var days = Days(rewind);
        var opened = await days.OpenOrCurrentAsync(ct);
        rewind.Time.Advance(TimeSpan.FromHours(5));

        var current = await days.OpenOrCurrentAsync(ct);

        Assert.Equal(opened, current);
    }

    [Fact]
    public async Task OpenOrCurrentAsync_AfterClosing_OpensANewOne()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var days = Days(rewind);
        var lastWeek = await days.OpenOrCurrentAsync(ct);
        await days.CloseAsync(ct);
        rewind.Time.Advance(TimeSpan.FromDays(7));

        var tonight = await days.OpenOrCurrentAsync(ct);

        Assert.NotEqual(lastWeek.Id, tonight.Id);
        Assert.Equal((Now(rewind), (DateTime?)null), (tonight.OpenedAt, tonight.ClosedAt));
    }

    // DATA-11: no night is open when the service looks, then another scan opens one before the save, and
    // the unique index refuses this one.
    [Fact]
    public async Task OpenOrCurrentAsync_OpenedDuringTheSave_ReturnsTheOtherOne()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var days = Days(rewind, new BeforeEachSave(rewind, cancellationToken => OpenANightAsync(rewind, cancellationToken)));

        var night = await days.OpenOrCurrentAsync(ct);

        Assert.Equal(Assert.Single(await SavedDaysAsync(rewind, ct)), night);
    }

    // NET-12, OBS-3: every scan of a night finds it open, which is no error. Left to the unique index,
    // EF Core would log each refused save as one.
    [Fact]
    public async Task OpenOrCurrentAsync_OneOpen_LogsNoError()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await OpenANightAsync(rewind, ct);
        var log = new ListLoggerProvider();
        using var logged = new LoggedContexts(rewind, log);

        await Days(rewind, logged).OpenOrCurrentAsync(ct);

        Assert.Contains(log.Entries, entry => entry.Category.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)); // the log is EF Core's
        Assert.DoesNotContain(log.Entries, entry => entry.Level >= LogLevel.Error);
    }

    // OBS-3: a line for each night opened, and none for each of its scans.
    [Fact]
    public async Task OpenOrCurrentAsync_OneOpen_LogsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await OpenANightAsync(rewind, ct);
        var log = new ListLogger<BusinessDays>();

        await new BusinessDays(rewind.DbFactory, rewind.Time, log).OpenOrCurrentAsync(ct);

        Assert.Empty(log.Entries);
    }

    // NET-7: the read hands its token on, so a screen that closes stops it.
    [Fact]
    public async Task OpenOrCurrentAsync_CancelledWithOneOpen_Stops()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await OpenANightAsync(rewind, ct);
        using var leaving = new CancellationTokenSource();
        await leaving.CancelAsync();

        var open = () => Days(rewind).OpenOrCurrentAsync(leaving.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(open);
    }

    // NET-7: the save hands its token on too.
    [Fact]
    public async Task OpenOrCurrentAsync_CancelledJustBeforeTheSave_OpensNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        using var leaving = new CancellationTokenSource();
        var days = Days(rewind, new BeforeEachSave(rewind, _ => leaving.CancelAsync()));

        var open = () => days.OpenOrCurrentAsync(leaving.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(open);
        Assert.Empty(await SavedDaysAsync(rewind, ct));
    }

    [Fact]
    public async Task CloseAsync_OneOpen_ClosesItAtTheClocksTime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var days = Days(rewind);
        var opened = await days.OpenOrCurrentAsync(ct);
        rewind.Time.Advance(TimeSpan.FromHours(3));

        var closing = await days.CloseAsync(ct);

        var closed = opened with { ClosedAt = Now(rewind) };
        Assert.Equal(new BusinessDayClosing(BusinessDayClosingStatus.Closed, closed), closing);
        Assert.Equal([closed], await SavedDaysAsync(rewind, ct));
    }

    // NET-12: Close Out still runs with no night open, so this is an answer, not an exception.
    [Fact]
    public async Task CloseAsync_NoneOpen_ReturnsNothingOpen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var closing = await Days(rewind).CloseAsync(ct);

        Assert.Equal(new BusinessDayClosing(BusinessDayClosingStatus.NothingOpen, null), closing);
        Assert.Empty(await SavedDaysAsync(rewind, ct));
    }

    // Build Plan 3.5: two Close Outs in one night make two backups, and the night stays closed at the first.
    [Fact]
    public async Task CloseAsync_Twice_KeepsTheFirstClosingTime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var days = Days(rewind);
        var opened = await days.OpenOrCurrentAsync(ct);
        await days.CloseAsync(ct);
        var closedAt = Now(rewind);
        rewind.Time.Advance(TimeSpan.FromMinutes(10));

        var second = await days.CloseAsync(ct);

        Assert.Equal(new BusinessDayClosing(BusinessDayClosingStatus.NothingOpen, null), second);
        Assert.Equal([opened with { ClosedAt = closedAt }], await SavedDaysAsync(rewind, ct));
    }

    // As for a member's rename, a change that changes nothing leaves no line in the log.
    [Fact]
    public async Task CloseAsync_NoneOpen_LogsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var log = new ListLogger<BusinessDays>();

        await new BusinessDays(rewind.DbFactory, rewind.Time, log).CloseAsync(ct);

        Assert.Empty(log.Entries);
    }

    // NET-7: the read hands its token on, so a screen that closes stops it.
    [Fact]
    public async Task CloseAsync_CancelledWithNoneOpen_Stops()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        using var leaving = new CancellationTokenSource();
        await leaving.CancelAsync();

        var close = () => Days(rewind).CloseAsync(leaving.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(close);
    }

    // NET-7: the save hands its token on too.
    [Fact]
    public async Task CloseAsync_CancelledJustBeforeTheSave_LeavesItOpen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var opened = await Days(rewind).OpenOrCurrentAsync(ct);
        using var leaving = new CancellationTokenSource();
        var days = Days(rewind, new BeforeEachSave(rewind, _ => leaving.CancelAsync()));

        var close = () => days.CloseAsync(leaving.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(close);
        Assert.Equal([opened], await SavedDaysAsync(rewind, ct));
    }

    // OBS-3: each night's start and end are what the rest of its log reads against.
    [Fact]
    public async Task EachChange_AnyNight_IsLogged()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var log = new ListLogger<BusinessDays>();
        var days = new BusinessDays(rewind.DbFactory, rewind.Time, log);

        await days.OpenOrCurrentAsync(ct);
        await days.CloseAsync(ct);

        Assert.Equal(
            [
                (LogLevel.Information, "Opened business day 1", (Exception?)null),
                (LogLevel.Information, "Closed business day 1", null),
            ],
            log.Entries);
    }

    private static BusinessDays Days(TempRewind rewind, IDbContextFactory<RewindDb>? dbFactory = null) =>
        new(dbFactory ?? rewind.DbFactory, rewind.Time, NullLogger<BusinessDays>.Instance);

    private static DateTime Now(TempRewind rewind) => rewind.Time.GetUtcNow().UtcDateTime;

    // A night opened before the test, or by another scan during it, saved straight to the database.
    private static async Task OpenANightAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        db.BusinessDays.Add(new BusinessDay { OpenedAt = Now(rewind) });
        await db.SaveChangesAsync(cancellationToken);
    }

    // The nights as the database holds them, oldest first.
    private static async Task<List<BusinessDayRow>> SavedDaysAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        return await db.BusinessDays
            .AsNoTracking()
            .OrderBy(d => d.Id)
            .Select(d => new BusinessDayRow(d.Id, d.OpenedAt, d.ClosedAt))
            .ToListAsync(cancellationToken);
    }
}
