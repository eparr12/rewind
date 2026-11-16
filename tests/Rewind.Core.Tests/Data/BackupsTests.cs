using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rewind.Core.Data;
using Rewind.Core.Members;

namespace Rewind.Core.Tests.Data;

public sealed class BackupsTests
{
    // Not databases: these tests only list, count and delete, which go by the name alone.
    private static readonly byte[] Backup = [0x53, 0x51, 0x4C, 0x69, 0x74, 0x65];
    private static readonly byte[] HalfABackup = [0x53, 0x51, 0x4C];

    [Fact]
    public async Task CreateAsync_OnMovieNight_NamesFileForLocalTime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        var backup = await rewind.Backups.CreateAsync(ct);

        Assert.Equal("rewind-2026-10-23-2215.db", Path.GetFileName(backup));
    }

    [Fact]
    public async Task CreateAsync_TwiceInOneMinute_KeepsTheFirstBackup()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var first = await rewind.Backups.CreateAsync(ct);

        var second = await rewind.Backups.CreateAsync(ct);

        Assert.Equal("rewind-2026-10-23-2215-2.db", Path.GetFileName(second));
        Assert.True(File.Exists(first));
    }

    [Fact]
    public async Task CreateAsync_NoDatabaseYet_ReturnsNullWithoutCreatingOne()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();

        var backup = await rewind.Backups.CreateAsync(ct);

        Assert.Null(backup);
        Assert.False(File.Exists(rewind.Paths.Database));
    }

    // The copy is made under another name and renamed once SQLite has closed it, so nothing is left beside it: not the
    // name it was made under, and not SQLite's journal. Dad was saved before the backup, so it's the family's database.
    [Fact]
    public async Task CreateAsync_Done_LeavesOneFileThatOpensAsTheDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using (var db = rewind.OpenDb())
        {
            db.Members.Add(new Member { Name = "Dad", CardBarcode = "MEM-001" });
            await db.SaveChangesAsync(ct);
        }

        var backup = await rewind.Backups.CreateAsync(ct);

        Assert.Equal(["rewind-2026-10-23-2215.db"], rewind.BackupFiles().Select(file => Path.GetFileName(file)));
        await using var copy = rewind.OpenDb(backup);
        Assert.Equal("Dad", (await copy.Members.SingleAsync(ct)).Name);
    }

    // SQLite makes the copy's file as it opens it, before it reads the database. This rewind.db isn't a database, so the
    // backup fails at the first page it reads, after that file is made. The backup deletes it and throws, so the folder
    // holds nothing: a backup made in place would leave a file with a backup's name, which would be listed, counted and
    // copied to the drive as one.
    [Fact]
    public async Task CreateAsync_BackupFails_LeavesNoFileBehind()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        Directory.CreateDirectory(rewind.Paths.Data);
        await File.WriteAllTextAsync(rewind.Paths.Database, "This isn't a database.", ct);

        await Assert.ThrowsAsync<SqliteException>(() => rewind.Backups.CreateAsync(ct));

        Assert.Empty(Directory.EnumerateFileSystemEntries(rewind.Paths.Backups));
    }

    // A crash or a power cut partway through a backup leaves it under the name it was being made under. A backup in that
    // minute takes the next name, and what was left stays as it was, since nothing but a backup is ever written over.
    [Fact]
    public async Task CreateAsync_UnfinishedBackupLeftInItsName_TakesTheNextNameAndLeavesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        var unfinished = await PutAsync(rewind, "rewind-2026-10-23-2215.db.partial", HalfABackup, ct);

        var backup = await rewind.Backups.CreateAsync(ct);

        Assert.Equal("rewind-2026-10-23-2215-2.db", Path.GetFileName(backup));
        Assert.Equal(HalfABackup, await File.ReadAllBytesAsync(unfinished, ct));
    }

    // By the time in the name, then the number: an ordinal sort would put -10 before -2, and -2 before the minute's
    // first. The night that ran past midnight comes after the evening before it.
    [Fact]
    public async Task List_SeveralBackups_AreInOrderOfTimeThenNumber()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutAllAsync(
            rewind,
            ct,
            "rewind-2026-10-23-2215-10.db",
            "rewind-2026-10-24-0005.db",
            "rewind-2026-10-23-2215.db",
            "rewind-2026-10-23-2215-2.db",
            "rewind-2025-12-31-2359.db");

        var backups = rewind.Backups.List();

        Assert.Equal(
            ["rewind-2025-12-31-2359.db", "rewind-2026-10-23-2215.db", "rewind-2026-10-23-2215-2.db", "rewind-2026-10-23-2215-10.db", "rewind-2026-10-24-0005.db"],
            backups.Select(backup => Path.GetFileName(backup)));
    }

    // The README says to keep the scanner driver's installer with the backups, and SQLite leaves a -wal file beside a
    // backup that's open, such as in the SQLite shell.
    [Fact]
    public async Task List_FilesThatArentBackups_LeavesThemOut()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutAllAsync(rewind, ct, "TEEMI-VCOM-Driver-Setup.exe", "notes.txt", "rewind-2026-10-16-2215.db-wal", "rewind-2026-10-23-2215.db");

        var backups = rewind.Backups.List();

        Assert.Equal([Path.Combine(rewind.Paths.Root, "backups", "rewind-2026-10-23-2215.db")], backups);
    }

    // What a crash or a power cut partway through a backup leaves: the copy under the name it was made under, and
    // SQLite's journal beside it.
    [Fact]
    public async Task List_UnfinishedBackup_LeavesItOut()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutAllAsync(rewind, ct, "rewind-2026-10-16-2215.db.partial", "rewind-2026-10-16-2215.db.partial-journal", "rewind-2026-10-23-2215.db");

        var backups = rewind.Backups.List();

        Assert.Equal(["rewind-2026-10-23-2215.db"], backups.Select(backup => Path.GetFileName(backup)));
    }

    // CreateAsync numbers a minute's second backup -2, with no leading 0, and every year has fewer than 13 months.
    [Fact]
    public async Task List_NamesCreateAsyncNeverGives_LeavesThemOut()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutAllAsync(
            rewind,
            ct,
            "rewind-2026-10-16-2215-1.db",
            "rewind-2026-10-16-2215-02.db",
            "rewind-2026-13-16-2215.db",
            "old-rewind-2026-10-16-2215.db",
            "rewind-2026-10-23-2215.db");

        var backups = rewind.Backups.List();

        Assert.Equal(["rewind-2026-10-23-2215.db"], backups.Select(backup => Path.GetFileName(backup)));
    }

    // Before the first backup, there's no backups folder.
    [Fact]
    public async Task List_NoBackupsYet_IsEmpty()
    {
        await using var rewind = new TempRewind();

        Assert.Empty(rewind.Backups.List());
    }

    [Fact]
    public async Task KeepNewest_ThirtyOneBackups_DeletesTheOldest()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutAllAsync(rewind, ct, NightsOfOctober(1, 31));

        var deleted = rewind.Backups.KeepNewest();

        Assert.Equal(1, deleted);
        Assert.Equal(NightsOfOctober(2, 31), BackupNames(rewind));
    }

    [Fact]
    public async Task KeepNewest_ThirtyBackups_DeletesNone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutAllAsync(rewind, ct, NightsOfOctober(2, 31));

        var deleted = rewind.Backups.KeepNewest();

        Assert.Equal(0, deleted);
        Assert.Equal(NightsOfOctober(2, 31), BackupNames(rewind));
    }

    // Two Close Outs in the oldest minute: the first one's backup is the older, though an ordinal sort puts its -2 first.
    [Fact]
    public async Task KeepNewest_OldestTwoFromOneMinute_DeletesTheFirstOfThem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutAllAsync(rewind, ct, ["rewind-2026-09-30-2215.db", "rewind-2026-09-30-2215-2.db", .. NightsOfOctober(1, 29)]);

        rewind.Backups.KeepNewest();

        Assert.Equal(["rewind-2026-09-30-2215-2.db", .. NightsOfOctober(1, 29)], BackupNames(rewind));
    }

    // Each is named to sort before every backup, so it would be the first to go if it were counted as one.
    [Fact]
    public async Task KeepNewest_FilesThatArentBackups_LeavesThem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        string[] others = ["TEEMI-VCOM-Driver-Setup.exe", "notes.txt", "rewind-2026-09-01-2215.db-wal"];
        await PutAllAsync(rewind, ct, [.. others, .. NightsOfOctober(1, 31)]);

        var deleted = rewind.Backups.KeepNewest();

        Assert.Equal(1, deleted);
        Assert.All(others, other => Assert.True(File.Exists(Path.Combine(rewind.Paths.Root, "backups", other)), other));
    }

    // Thirty backups and what an interrupted one left: if that counted, there'd be one too many.
    [Fact]
    public async Task KeepNewest_UnfinishedBackup_DoesntCountIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutAllAsync(rewind, ct, ["rewind-2026-09-30-2215.db.partial", .. NightsOfOctober(2, 31)]);

        var deleted = rewind.Backups.KeepNewest();

        Assert.Equal(0, deleted);
        Assert.Equal(["rewind-2026-09-30-2215.db.partial", .. NightsOfOctober(2, 31)], BackupNames(rewind));
    }

    // The oldest of 32 is open in another program, so it can't go, and the next oldest still does. The count is of the
    // backups that went, and the log names the one that stayed as well as the one that went.
    [Fact]
    public async Task KeepNewest_BackupItCantDelete_WarnsAndStillDeletesTheRest()
    {
        // Linux deletes a file that's open, so nothing there stands in for a backup that can't be deleted.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to delete a file that's open.");
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutAllAsync(rewind, ct, ["rewind-2026-09-30-2215.db", .. NightsOfOctober(1, 31)]);
        var held = Path.Combine(rewind.Paths.Root, "backups", "rewind-2026-09-30-2215.db");
        var log = new ListLogger<Backups>();
        int deleted;

        await using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            deleted = BackupsFor(rewind, log).KeepNewest();
        }

        Assert.Equal(1, deleted);
        Assert.Equal(["rewind-2026-09-30-2215.db", .. NightsOfOctober(2, 31)], BackupNames(rewind));
        var next = Path.Combine(rewind.Paths.Root, "backups", "rewind-2026-10-01-2215.db");
        Assert.Equal(
            [
                (LogLevel.Warning, $"Couldn't delete the old backup {held}, such as when another program has it open; it's tried again next time"),
                (LogLevel.Information, $"Deleted the backup {next}, the oldest past the newest 30"),
            ],
            log.Entries.Select(entry => (entry.Level, entry.Message)));
    }

    // Information (OBS-3): a backup is the family's history, so the log names each one that goes.
    [Fact]
    public async Task KeepNewest_BackupDeleted_LogsWhichOne()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutAllAsync(rewind, ct, NightsOfOctober(1, 31));
        var log = new ListLogger<Backups>();

        BackupsFor(rewind, log).KeepNewest();

        var oldest = Path.Combine(rewind.Paths.Root, "backups", "rewind-2026-10-01-2215.db");
        Assert.Equal([(LogLevel.Information, $"Deleted the backup {oldest}, the oldest past the newest 30", (Exception?)null)], log.Entries);
    }

    private static Backups BackupsFor(TempRewind rewind, ILogger<Backups> logger) => new(Options.Create(rewind.Paths), rewind.Time, logger);

    // One backup a night at 10:15 PM, from one day of October to another, oldest first.
    private static string[] NightsOfOctober(int first, int last) =>
        [.. Enumerable.Range(first, last - first + 1).Select(day => $"rewind-2026-10-{day:00}-2215.db")];

    // Every file the backups folder holds, by name, in order by character code.
    private static List<string> BackupNames(TempRewind rewind) =>
        [.. rewind.BackupFiles().Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal)];

    // A file in the backups folder, written straight there as a stand-in.
    private static async Task<string> PutAsync(TempRewind rewind, string name, byte[] bytes, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(rewind.Paths.Root, "backups");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        return path;
    }

    private static async Task PutAllAsync(TempRewind rewind, CancellationToken cancellationToken, params string[] names)
    {
        foreach (var name in names)
        {
            await PutAsync(rewind, name, Backup, cancellationToken);
        }
    }
}
