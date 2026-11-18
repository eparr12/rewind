using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rewind.Core.Catalog;
using Rewind.Core.Checkout;
using Rewind.Core.CloseOut;
using Rewind.Core.Data;
using Rewind.Core.Devices;
using Rewind.Core.Members;
using Rewind.Core.MovieNights;
using Rewind.Core.Returns;

namespace Rewind.Core.Tests.CloseOut;

public sealed class CloseOutRegisterTests
{
    private const string Label = "RV-00042";
    private const string Marker = "REWIND-BACKUP.txt";
    private const int RentalPriceCents = 200;

    // TempRewind's clock says 10:15 PM on October 23, 2026, so a backup made then has tonight's name.
    private const string Tonight = "rewind-2026-10-23-2215.db";
    private const string LastWeek = "rewind-2026-10-16-2215.db";

    // The warnings, word for word, so each test can say whose it is.
    private const string NoDriveWarning = "Found no backup drive: no drive has REWIND-BACKUP.txt in its top folder. The register's own disk never counts";
    private const string BackupWarning = "Close Out couldn't back up the database, so what's changed since the last backup is in none yet; the next Close Out makes a new one";
    private const string CatalogWarning = "Close Out couldn't write the phone catalog, so the outbox keeps the last one written; the next Close Out writes a new one";
    private const string CopyWarning = "Close Out couldn't copy the backups to the backup drive, such as when another program has one of them open on the register; the next Close Out copies whatever the drive is missing";
    private const string KeepNewestWarning = "Close Out couldn't delete the backups past the newest 30; the next Close Out tries again";

    // A stand-in for an older backup: the copy goes by name and length, and never opens one.
    private static readonly byte[] OlderBackup = [0x53, 0x51, 0x4C, 0x69, 0x74, 0x65, 0x16];

    // Real time, not fake: how long a test waits for a Close Out that should be done by then, so one stuck behind
    // another fails the test after 10 seconds instead of hanging the run.
    private static readonly TimeSpan NoHang = TimeSpan.FromSeconds(10);

    // Build Plan 3.5: the night closed, tonight's backup made and copied, and the catalog written with the one movie
    // on the shelf.
    [Fact]
    public async Task RunAsync_EverythingWorks_ReportsEachStepDone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await AddCaseAsync(rewind, ct);
        await Days(rewind).OpenOrCurrentAsync(ct);
        rewind.Drives.PlugInBackupDrive("E");
        using var closeOut = CloseOutFor(rewind);

        var report = await closeOut.RunAsync(ct);

        Assert.Equal((BusinessDayClosingStatus.Closed, Tonight, 1), (report.Night, report.Backup, report.CatalogMovies));
        Assert.Equal((DriveCopyStatus.Copied, 1), (report.Copy?.Status, report.Copy?.Files));
        Assert.True(report.EverythingWorked);
    }

    // Build Plan 3.5's Done when: two Close Outs in one night make two backups. The first closes the night, the
    // second finds none open, and the night keeps its first closing time.
    [Fact]
    public async Task RunAsync_TwiceInOneNight_MakesTwoBackupsAndClosesTheNightOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var night = await Days(rewind).OpenOrCurrentAsync(ct);
        using var closeOut = CloseOutFor(rewind);
        var first = await closeOut.RunAsync(ct);
        var closedAt = Now(rewind);
        rewind.Time.Advance(TimeSpan.FromMinutes(10));

        var second = await closeOut.RunAsync(ct).WaitAsync(NoHang, ct);

        Assert.Equal((BusinessDayClosingStatus.Closed, Tonight), (first.Night, first.Backup));
        Assert.Equal((BusinessDayClosingStatus.NothingOpen, "rewind-2026-10-23-2225.db"), (second.Night, second.Backup));
        Assert.Equal([Tonight, "rewind-2026-10-23-2225.db"], RegisterBackups(rewind));
        Assert.Equal([night with { ClosedAt = closedAt }], await SavedDaysAsync(rewind, ct));
    }

    // Build Plan 3.5's Done when: a missing USB drive shows a warning instead of an error. Nothing is thrown, the rest
    // is done, down to keeping the newest 30, and the copy's own warning is the only one.
    [Fact]
    public async Task RunAsync_NoBackupDrive_SaysSoWithOneWarningAndDoesTheRest()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await Days(rewind).OpenOrCurrentAsync(ct);
        var oldest = await PutThirtyEarlierBackupsAsync(rewind, ct);
        var logs = new Logs();
        using var closeOut = CloseOutFor(rewind, logs: logs);

        var report = await closeOut.RunAsync(ct);

        Assert.Equal(DriveCopyStatus.NoDrive, report.Copy?.Status);
        Assert.False(report.EverythingWorked);
        Assert.Equal((BusinessDayClosingStatus.Closed, Tonight, 0), (report.Night, report.Backup, report.CatalogMovies));
        Assert.Equal((Backups.Kept, true, false), KeptOnTheRegister(rewind, oldest));
        Assert.Equal([(nameof(BackupDrive), LogLevel.Warning, NoDriveWarning)], logs.WarningsAndUp());
    }

    // A return opens no night, so a night of returns only has none to close, and its watch log needs backing up as much
    // as a rental does (ADR-0013). Halloween was rented last week, on a night that was closed out, and comes back
    // tonight through the real return desk.
    [Fact]
    public async Task RunAsync_NightOfReturnsOnly_BacksUpTheReturn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await RentLastWeekAsync(rewind, ct);
        Assert.Equal(CheckInStatus.CheckedIn, (await Desk(rewind).CheckInAsync(Label, ct)).Status);
        using var closeOut = CloseOutFor(rewind);

        var report = await closeOut.RunAsync(ct);

        Assert.Equal((BusinessDayClosingStatus.NothingOpen, Tonight), (report.Night, report.Backup));
        await using var backup = rewind.OpenDb(Path.Combine(rewind.Paths.Backups, Tonight));
        Assert.Equal(Now(rewind), (await backup.RentalItems.AsNoTracking().SingleAsync(ct)).ReturnedAt);
    }

    // The backup comes after closing, so it holds the night as closed, three hours after it opened.
    [Fact]
    public async Task RunAsync_NightOpen_BacksUpTheClosedNight()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var night = await Days(rewind).OpenOrCurrentAsync(ct);
        rewind.Time.Advance(TimeSpan.FromHours(3));
        using var closeOut = CloseOutFor(rewind);

        var report = await closeOut.RunAsync(ct);

        await using var backup = rewind.OpenDb(Path.Combine(rewind.Paths.Backups, Assert.IsType<string>(report.Backup)));
        var saved = await backup.BusinessDays.AsNoTracking().SingleAsync(ct);
        Assert.Equal((night.Id, Now(rewind)), (saved.Id, saved.ClosedAt));
    }

    // ADR-0014: nobody closed out last week's night, so tonight's first card ended it. Tonight's Close Out closes
    // tonight's night, and last week's keeps the time that card closed it.
    [Fact]
    public async Task RunAsync_AfterANightNobodyClosedOut_ClosesOnlyTonightsNight()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var lastWeek = await Days(rewind).OpenOrCurrentAsync(ct);
        rewind.Time.Advance(TimeSpan.FromDays(7));
        var tonight = await Days(rewind).OpenOrCurrentAsync(ct);
        var endedAt = Now(rewind);
        rewind.Time.Advance(TimeSpan.FromHours(3));
        using var closeOut = CloseOutFor(rewind);

        await closeOut.RunAsync(ct);

        Assert.Equal([lastWeek with { ClosedAt = endedAt }, tonight with { ClosedAt = Now(rewind) }], await SavedDaysAsync(rewind, ct));
    }

    // GEN-7: a file stands where the backups folder goes, so the backup can't be made, on Windows or on Linux. The
    // catalog is still written, the copy finds no backup to copy, and the one warning is the backup's, with what was
    // thrown (OBS-4).
    [Fact]
    public async Task RunAsync_BackupFails_ReportsItAndDoesTheRest()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await Days(rewind).OpenOrCurrentAsync(ct);
        rewind.Drives.PlugInBackupDrive("E");
        await File.WriteAllTextAsync(rewind.Paths.Backups, "", ct);
        var logs = new Logs();
        using var closeOut = CloseOutFor(rewind, logs: logs);

        var report = await closeOut.RunAsync(ct);

        Assert.Null(report.Backup);
        Assert.Equal((BusinessDayClosingStatus.Closed, 0, DriveCopyStatus.NothingToCopy), (report.Night, report.CatalogMovies, report.Copy?.Status));
        Assert.False(report.EverythingWorked);
        Assert.Equal([(nameof(CloseOutRegister), LogLevel.Warning, BackupWarning)], logs.WarningsAndUp());
        Assert.IsAssignableFrom<IOException>(logs.WarningFromCloseOut().Exception);
    }

    // GEN-7: a file stands where the outbox goes, so the catalog can't be written. Tonight's backup is still made and
    // copied, with the 30 before it, the oldest of them is still deleted, and the one warning is the catalog's, with
    // what was thrown (OBS-4).
    [Fact]
    public async Task RunAsync_CatalogFails_ReportsItAndDoesTheRest()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await Days(rewind).OpenOrCurrentAsync(ct);
        rewind.Drives.PlugInBackupDrive("E");
        var oldest = await PutThirtyEarlierBackupsAsync(rewind, ct);
        await File.WriteAllTextAsync(rewind.Paths.Outbox, "", ct);
        var logs = new Logs();
        using var closeOut = CloseOutFor(rewind, logs: logs);

        var report = await closeOut.RunAsync(ct);

        Assert.Null(report.CatalogMovies);
        Assert.Equal((BusinessDayClosingStatus.Closed, Tonight, DriveCopyStatus.Copied), (report.Night, report.Backup, report.Copy?.Status));
        Assert.False(report.EverythingWorked);
        Assert.Equal((Backups.Kept, true, false), KeptOnTheRegister(rewind, oldest));
        Assert.Equal([(nameof(CloseOutRegister), LogLevel.Warning, CatalogWarning)], logs.WarningsAndUp());
        Assert.IsAssignableFrom<IOException>(logs.WarningFromCloseOut().Exception);
    }

    // GEN-7: another program has last week's backup open on the register, and lets nobody else read it, so the copy
    // stops there and throws, since the register's own disk failing is no fault of the drive's (ADR-0013). The newest
    // goes first, so tonight's is on the drive already, and last week's isn't. Tonight's backup and the catalog are
    // still done, and the oldest backup is still deleted. The one warning is Close Out's, with what was thrown, since
    // the copy doesn't log what it throws (OBS-4).
    [Fact]
    public async Task RunAsync_CopyThrows_ReportsItAndDoesTheRest()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await Days(rewind).OpenOrCurrentAsync(ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");
        var oldest = await PutThirtyEarlierBackupsAsync(rewind, ct);
        await using var held = new FileStream(Path.Combine(rewind.Paths.Backups, LastWeek), FileMode.Open, FileAccess.Read, FileShare.None);
        var logs = new Logs();
        using var closeOut = CloseOutFor(rewind, logs: logs);

        var report = await closeOut.RunAsync(ct);

        Assert.Null(report.Copy);
        Assert.Equal((true, false), (DriveFiles(rewind, drive).Contains(Tonight), DriveFiles(rewind, drive).Contains(LastWeek)));
        Assert.Equal((BusinessDayClosingStatus.Closed, Tonight, 0), (report.Night, report.Backup, report.CatalogMovies));
        Assert.False(report.EverythingWorked);
        Assert.Equal((Backups.Kept, true, false), KeptOnTheRegister(rewind, oldest));
        Assert.Equal([(nameof(CloseOutRegister), LogLevel.Warning, CopyWarning)], logs.WarningsAndUp());
        Assert.IsAssignableFrom<IOException>(logs.WarningFromCloseOut().Exception);
    }

    // GEN-7: a file stands where E's folder for this root goes, as on a drive that's read-only, so the copy answers that
    // it couldn't write there. The rest is done, down to deleting the oldest backup, and the copy's own warning is the
    // only one: Close Out doesn't log it again (OBS-4).
    [Fact]
    public async Task RunAsync_DriveCantBeWrittenTo_ReportsItWithTheCopysOwnWarning()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await Days(rewind).OpenOrCurrentAsync(ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");
        await File.WriteAllTextAsync(Path.Combine(drive, Path.GetFileName(rewind.Paths.Root)), "", ct);
        var oldest = await PutThirtyEarlierBackupsAsync(rewind, ct);
        var logs = new Logs();
        using var closeOut = CloseOutFor(rewind, logs: logs);

        var report = await closeOut.RunAsync(ct);

        Assert.Equal(DriveCopyStatus.Unwritable, report.Copy?.Status);
        Assert.Equal([drive], report.Copy?.Unwritable);
        Assert.Equal((BusinessDayClosingStatus.Closed, Tonight, 0), (report.Night, report.Backup, report.CatalogMovies));
        Assert.False(report.EverythingWorked);
        Assert.Equal((Backups.Kept, true, false), KeptOnTheRegister(rewind, oldest));
        Assert.Equal(
            [(nameof(BackupDrive), LogLevel.Warning, $"Couldn't write to the backup drive {drive}, which may be full, read-only or pulled out, so it may still be missing backups")],
            logs.WarningsAndUp());
    }

    // GEN-7: keeping the newest backups is tried like the other steps, and isn't on the report. The backups folder can't
    // be listed, as after its permissions were changed by hand, though a backup can still be made in it. With no backup
    // drive, the copy never lists the folder, so counting the backups is the only step that fails.
    [Fact]
    public async Task RunAsync_BackupsCantBeCounted_WarnsAndStillReports()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "Root lists any folder on Linux.");
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await Days(rewind).OpenOrCurrentAsync(ct);
        Directory.CreateDirectory(rewind.Paths.Backups);
        using var unlistable = Unlistable.Make(rewind.Paths.Backups);
        var logs = new Logs();
        using var closeOut = CloseOutFor(rewind, logs: logs);

        var report = await closeOut.RunAsync(ct);

        Assert.Equal((BusinessDayClosingStatus.Closed, Tonight, 0, DriveCopyStatus.NoDrive), (report.Night, report.Backup, report.CatalogMovies, report.Copy?.Status));
        Assert.Equal(
            [
                (nameof(BackupDrive), LogLevel.Warning, NoDriveWarning),
                (nameof(CloseOutRegister), LogLevel.Warning, KeepNewestWarning),
            ],
            logs.WarningsAndUp());
        Assert.IsAssignableFrom<UnauthorizedAccessException>(logs.WarningFromCloseOut().Exception);
    }

    // Only closing the night can fail a Close Out (ADR-0013): when it throws, so does the call, and nothing else has
    // happened. The night's save fails, as on a disk that's gone.
    [Fact]
    public async Task RunAsync_ClosingTheNightFails_ThrowsAndDoesNothingElse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var night = await Days(rewind).OpenOrCurrentAsync(ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");
        using var closeOut = CloseOutFor(rewind, new BeforeEachSave(rewind, _ => throw new IOException("The disk is gone.")));

        var run = () => closeOut.RunAsync(ct);

        await Assert.ThrowsAsync<IOException>(run);
        Assert.Equal([night], await SavedDaysAsync(rewind, ct));
        Assert.Empty(rewind.BackupFiles());
        Assert.False(Path.Exists(rewind.Paths.Outbox));
        Assert.Equal([Marker], DriveEntries(drive));
    }

    // The register keeps its newest 30, counted after the copy (ADR-0013), so the oldest still reaches the drive before
    // it goes. Thirty earlier nights' backups are on the register, and the drive has none of them yet.
    [Fact]
    public async Task RunAsync_ThirtyFirstBackup_DeletesTheOldestOnceTheDriveHasIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await Days(rewind).OpenOrCurrentAsync(ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");
        var oldest = await PutThirtyEarlierBackupsAsync(rewind, ct);
        using var closeOut = CloseOutFor(rewind);

        var report = await closeOut.RunAsync(ct);

        Assert.Equal((Backups.Kept, true, false), KeptOnTheRegister(rewind, oldest));
        Assert.Equal((Backups.Kept + 1, true), (DriveFiles(rewind, drive).Count, DriveFiles(rewind, drive).Contains(oldest)));
        Assert.Equal(Backups.Kept + 1, report.Copy?.Files);
    }

    // NET-7: the clerk leaves just before the night's save, so nothing is closed, and nothing is written: no backup, no
    // catalog and no copy.
    [Fact]
    public async Task RunAsync_CancelledBeforeTheNightCloses_ClosesAndWritesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var night = await Days(rewind).OpenOrCurrentAsync(ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");
        using var leaving = new CancellationTokenSource();
        using var closeOut = CloseOutFor(rewind, new BeforeEachSave(rewind, _ => leaving.CancelAsync()));

        var run = () => closeOut.RunAsync(leaving.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(run);
        Assert.Equal([night], await SavedDaysAsync(rewind, ct));
        Assert.Empty(rewind.BackupFiles());
        Assert.False(Path.Exists(rewind.Paths.Outbox));
        Assert.Equal([Marker], DriveEntries(drive));
    }

    // NET-7: a Close Out stopped before the night closed leaves the way clear, so the clerk can press it again, and the
    // second one closes the night.
    [Fact]
    public async Task RunAsync_AgainAfterOneWasCancelled_ClosesTheNight()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await Days(rewind).OpenOrCurrentAsync(ct);
        using var leaving = new CancellationTokenSource();
        var saves = 0;
        using var closeOut = CloseOutFor(rewind, new BeforeEachSave(rewind, _ => Interlocked.Increment(ref saves) == 1 ? leaving.CancelAsync() : Task.CompletedTask));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => closeOut.RunAsync(leaving.Token));

        var again = await closeOut.RunAsync(ct).WaitAsync(NoHang, ct);

        Assert.Equal((BusinessDayClosingStatus.Closed, Tonight), (again.Night, again.Backup));
    }

    // NET-7: the clerk leaves just as the night's close is saved. The night is closed whatever happens next, so the rest
    // runs, and the report comes back with the backup made and every step done.
    [Fact]
    public async Task RunAsync_CancelledJustAfterTheNightCloses_StillReportsWithTheBackupMade()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await Days(rewind).OpenOrCurrentAsync(ct);
        rewind.Drives.PlugInBackupDrive("E");
        using var leaving = new CancellationTokenSource();
        using var closeOut = CloseOutFor(rewind, new AfterEachSave(rewind, _ => leaving.CancelAsync()));

        var report = await closeOut.RunAsync(leaving.Token);

        Assert.True(leaving.IsCancellationRequested); // the cancel came, once the night was closed
        Assert.Equal((BusinessDayClosingStatus.Closed, Tonight), (report.Night, report.Backup));
        Assert.True(report.EverythingWorked);
        Assert.Equal([Tonight], RegisterBackups(rewind));
    }

    // Two never overlap: a second Close Out waits until the first is done, steps and all, then runs, so it finds no night
    // open and makes a backup of its own. The first is held in its copy, with its night closed, while the second is
    // asked for. A second that didn't wait would ask for a context at once, to close the night.
    [Fact]
    public async Task RunAsync_TwoAtOnce_SecondWaitsUntilTheFirstIsDone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await Days(rewind).OpenOrCurrentAsync(ct);
        rewind.Drives.PlugInBackupDrive("E");
        var contexts = new CountedContexts(rewind.DbFactory);
        using var drives = new HeldDrives(rewind.Drives);
        using var closeOut = CloseOutFor(rewind, contexts, drives: drives);
        var first = closeOut.RunAsync(ct);
        await drives.Listing.WaitAsync(NoHang, ct);
        var madeBeforeTheSecond = contexts.Made;

        var second = closeOut.RunAsync(ct);
        var madeByTheSecondMeanwhile = contexts.Made - madeBeforeTheSecond;
        drives.LetGo();
        var reports = await Task.WhenAll(first, second).WaitAsync(NoHang, ct);

        Assert.Equal(0, madeByTheSecondMeanwhile);
        Assert.Equal([BusinessDayClosingStatus.Closed, BusinessDayClosingStatus.NothingOpen], reports.Select(report => report.Night));
        Assert.Equal([Tonight, "rewind-2026-10-23-2215-2.db"], reports.Select(report => report.Backup));
    }

    // NET-7: a Close Out waiting for another hasn't closed anything yet, so a cancel still stops it, at once, and it
    // makes no backup once the first is done.
    [Fact]
    public async Task RunAsync_CancelledWhileWaitingForAnother_ThrowsAndDoesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await Days(rewind).OpenOrCurrentAsync(ct);
        var held = new TaskCompletionSource();
        using var closeOut = CloseOutFor(rewind, new BeforeEachSave(rewind, _ => held.Task));
        var first = closeOut.RunAsync(ct);
        using var leaving = new CancellationTokenSource();
        var second = closeOut.RunAsync(leaving.Token);

        await leaving.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.WaitAsync(NoHang, ct));
        held.SetResult();
        Assert.Equal(Tonight, (await first.WaitAsync(NoHang, ct)).Backup);
        Assert.Equal([Tonight], RegisterBackups(rewind));
    }

    // OBS-3: one line for each Close Out, saying what it came to, at Information, as far down as the register's log
    // goes unless Log:MinimumLevel says otherwise.
    [Fact]
    public async Task RunAsync_EverythingWorks_LogsWhatItCameToAtInformation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await Days(rewind).OpenOrCurrentAsync(ct);
        rewind.Drives.PlugInBackupDrive("E");
        var log = new ListLoggerProvider();
        using var loggers = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Information).AddProvider(log));
        using var closeOut = CloseOutFor(rewind, logger: loggers.CreateLogger<CloseOutRegister>());

        await closeOut.RunAsync(ct);

        Assert.Equal(
            [(LogLevel.Information, $"Closed out the register: night Closed, backup {Tonight}, phone catalog written, copy to the backup drive Copied")],
            log.Entries.Select(entry => (entry.Level, entry.Message)));
    }

    // OBS-3: a step that wasn't done is named in words, beside the warning that says why. No night is open, a folder
    // stands where tonight's backup goes, a file where the outbox goes, and another program has last week's backup open.
    [Fact]
    public async Task RunAsync_NothingWorks_LogsWhatWasntDone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        rewind.Drives.PlugInBackupDrive("E");
        Directory.CreateDirectory(Path.Combine(rewind.Paths.Backups, Tonight));
        await File.WriteAllTextAsync(rewind.Paths.Outbox, "", ct);
        await PutBackupAsync(rewind, LastWeek, ct);
        await using var held = new FileStream(Path.Combine(rewind.Paths.Backups, LastWeek), FileMode.Open, FileAccess.Read, FileShare.None);
        var logs = new Logs();
        using var closeOut = CloseOutFor(rewind, logs: logs);

        await closeOut.RunAsync(ct);

        Assert.Equal(
            "Closed out the register: night NothingOpen, backup not saved, phone catalog not written, copy to the backup drive failed",
            Assert.Single(logs.CloseOut.Entries, entry => entry.Level == LogLevel.Information).Message);
    }

    // The Close Out for the test's root folder, over the real business days, backups, phone catalog and copy, given the
    // test's clock and drives (TEST-4, TEST-5), or a stand-in wrapping those drives. The test's contexts stand in for
    // the database's when it gives some, and its own logger for Close Out's lines.
    private static CloseOutRegister CloseOutFor(
        TempRewind rewind,
        IDbContextFactory<RewindDb>? dbFactory = null,
        Logs? logs = null,
        ILogger<CloseOutRegister>? logger = null,
        IDrives? drives = null)
    {
        var db = dbFactory ?? rewind.DbFactory;
        var log = logs ?? new Logs();
        var options = Options.Create(rewind.Paths);
        var backups = new Backups(options, rewind.Time, log.Backups);
        return new(
            new BusinessDays(db, rewind.Time, log.Night),
            backups,
            new PhoneCatalog(db, options, rewind.Time, log.Catalog),
            new BackupDrive(backups, drives ?? rewind.Drives, options, log.Copy),
            logger ?? log.CloseOut);
    }

    private static BusinessDays Days(TempRewind rewind) => new(rewind.DbFactory, rewind.Time, NullLogger<BusinessDays>.Instance);

    private static ReturnDesk Desk(TempRewind rewind) =>
        new(rewind.DbFactory, new MemberRoster(rewind.DbFactory, NullLogger<MemberRoster>.Instance), rewind.Time, NullLogger<ReturnDesk>.Instance);

    private static DateTime Now(TempRewind rewind) => rewind.Time.GetUtcNow().UtcDateTime;

    // Halloween, in a case on the shelf, so the catalog lists one movie.
    private static async Task AddCaseAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        await using var db = rewind.OpenDb();
        db.Cases.Add(new Case { StoreLabel = Label, Movies = { new Movie { Title = "Halloween", Year = 1978 } } });
        await db.SaveChangesAsync(cancellationToken);
    }

    // Dad rented Halloween a week ago, on a night that was closed out, so no night is open now. Written straight to the
    // database, as the counter saves a sale: the rental, its item, and the case checked out.
    private static async Task RentLastWeekAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        var lastWeek = Now(rewind) - TimeSpan.FromDays(7);
        await using var db = rewind.OpenDb();
        var dad = new Member { Name = "Dad", CardBarcode = "MEM-001" };
        var halloween = new Movie { Title = "Halloween", Year = 1978 };
        var rented = new Case { StoreLabel = Label, Status = CaseStatus.CheckedOut, Movies = { halloween } };
        var night = new BusinessDay { OpenedAt = lastWeek, ClosedAt = lastWeek + TimeSpan.FromHours(3) };
        db.AddRange(dad, rented, night);
        await db.SaveChangesAsync(cancellationToken);
        db.Rentals.Add(new Rental
        {
            MemberId = dad.Id,
            BusinessDayId = night.Id,
            RentedAt = lastWeek,
            TotalCents = RentalPriceCents,
            CashTenderedCents = RentalPriceCents,
            Items = { new RentalItem { CaseId = rented.Id, MovieId = halloween.Id, PriceCents = RentalPriceCents } },
        });
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

    // An older backup on the register, written straight to the backups folder as a stand-in.
    private static async Task PutBackupAsync(TempRewind rewind, string name, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(rewind.Paths.Backups);
        await File.WriteAllBytesAsync(Path.Combine(rewind.Paths.Backups, name), OlderBackup, cancellationToken);
    }

    // The 30 nights before tonight, a backup each, last week's among them, so tonight's is the 31st and keeping the
    // newest 30 deletes the oldest. Returns the oldest's name.
    private static async Task<string> PutThirtyEarlierBackupsAsync(TempRewind rewind, CancellationToken cancellationToken)
    {
        var names = Enumerable.Range(1, Backups.Kept)
            .Select(nightsAgo => new DateOnly(2026, 10, 23).AddDays(-nightsAgo).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Select(night => $"rewind-{night}-2215.db")
            .ToList();
        foreach (var name in names)
        {
            await PutBackupAsync(rewind, name, cancellationToken);
        }

        return names[^1];
    }

    // How many backups the register has, whether tonight's is among them, and whether the oldest is still there.
    private static (int Count, bool HasTonights, bool HasTheOldest) KeptOnTheRegister(TempRewind rewind, string oldest)
    {
        var backups = RegisterBackups(rewind);
        return (backups.Count, backups.Contains(Tonight), backups.Contains(oldest));
    }

    // The name of each file in the register's backups folder, in order by character code.
    private static List<string> RegisterBackups(TempRewind rewind) =>
        [.. rewind.BackupFiles().Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal)];

    // The name of each file in the drive's folder for this root, E:\<the root folder's name>\backups, written out here
    // rather than taken from RewindPaths. Empty when there's no folder.
    private static List<string> DriveFiles(TempRewind rewind, string drive)
    {
        var folder = Path.Combine(drive, Path.GetFileName(rewind.Paths.Root), "backups");
        return Directory.Exists(folder)
            ? [.. Directory.GetFiles(folder).Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal)]
            : [];
    }

    // The name of everything in the drive's top folder.
    private static List<string> DriveEntries(string drive) =>
        [.. Directory.EnumerateFileSystemEntries(drive).Select(entry => Path.GetFileName(entry))];

    // Each piece's own log, so a test can tell whose a line is.
    private sealed class Logs
    {
        public ListLogger<BusinessDays> Night { get; } = new();

        public ListLogger<Backups> Backups { get; } = new();

        public ListLogger<PhoneCatalog> Catalog { get; } = new();

        public ListLogger<BackupDrive> Copy { get; } = new();

        public ListLogger<CloseOutRegister> CloseOut { get; } = new();

        // Every line at Warning or above, as whose it is and what it says: the steps' own, in the order Close Out runs
        // them, then Close Out's.
        public List<(string From, LogLevel Level, string Message)> WarningsAndUp() =>
        [
            .. WarningsAndUp(nameof(BusinessDays), Night.Entries),
            .. WarningsAndUp(nameof(Rewind.Core.Data.Backups), Backups.Entries),
            .. WarningsAndUp(nameof(PhoneCatalog), Catalog.Entries),
            .. WarningsAndUp(nameof(BackupDrive), Copy.Entries),
            .. WarningsAndUp(nameof(CloseOutRegister), CloseOut.Entries),
        ];

        // Close Out's one warning, with the exception it was logged with.
        public (LogLevel Level, string Message, Exception? Exception) WarningFromCloseOut() =>
            Assert.Single(CloseOut.Entries, entry => entry.Level == LogLevel.Warning);

        private static IEnumerable<(string From, LogLevel Level, string Message)> WarningsAndUp(
            string from,
            IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> entries) =>
            entries.Where(entry => entry.Level >= LogLevel.Warning).Select(entry => (from, entry.Level, entry.Message));
    }

    // The test's contexts, counted as they're made, so a test can tell whether a Close Out has started: closing the night
    // asks for one first.
    private sealed class CountedContexts(IDbContextFactory<RewindDb> contexts) : IDbContextFactory<RewindDb>
    {
        private int _made;

        public int Made => Volatile.Read(ref _made);

        public RewindDb CreateDbContext()
        {
            Interlocked.Increment(ref _made);
            return contexts.CreateDbContext();
        }
    }

    // The test's drives, which hold the copy that lists them until the test lets it go, and say when one is waiting. The
    // copy lists them on the thread pool, so holding it there keeps the test's own thread free.
    private sealed class HeldDrives(IDrives drives) : IDrives, IDisposable
    {
        private readonly ManualResetEventSlim _letGo = new();
        private readonly TaskCompletionSource _listing = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Listing => _listing.Task;

        public IReadOnlyList<string> TopFolders()
        {
            _listing.TrySetResult();
            _letGo.Wait(NoHang); // real time, not fake: a copy the test never lets go fails it instead of hanging the run
            return drives.TopFolders();
        }

        public void LetGo() => _letGo.Set();

        public void Dispose() => _letGo.Dispose();
    }
}
