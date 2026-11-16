using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rewind.Core.CloseOut;
using Rewind.Core.Data;
using Rewind.Core.Devices;

namespace Rewind.Core.Tests.CloseOut;

public sealed class BackupDriveTests
{
    private const string Tonight = "rewind-2026-10-23-2215.db";
    private const string LastWeek = "rewind-2026-10-16-2215.db";

    // Stand-ins for backups, apart from the one CreateAsync makes: the copy goes by name and length, and never opens one.
    // TonightsBackup and NotTonightsBackup are the same length, and only their bytes tell them apart.
    private static readonly byte[] TonightsBackup = [0x53, 0x51, 0x4C, 0x69, 0x74, 0x65, 0x23];
    private static readonly byte[] NotTonightsBackup = [0x53, 0x51, 0x4C, 0x69, 0x74, 0x65, 0x99];
    private static readonly byte[] LastWeeksBackup = [0x53, 0x51, 0x4C, 0x69, 0x74, 0x65, 0x16, 0x16];
    private static readonly byte[] HalfABackup = [0x53, 0x51, 0x4C];

    // A file time from long before any test runs, to tell whether a copy wrote over a file.
    private static readonly DateTime LongAgo = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // TEST-5: so Rewind never writes to a random flash drive.
    [Fact]
    public async Task CopyAsync_DriveWithoutTheMarker_IsNeverWrittenTo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        var drive = rewind.Drives.PlugIn("E");

        await DriveFor(rewind).CopyAsync(ct);

        Assert.Empty(Directory.EnumerateFileSystemEntries(drive));
    }

    // Build Plan 3.5: the backup CreateAsync made, in E:\<the root folder's name>\backups, so a test's backups never sit
    // among the store's.
    [Fact]
    public async Task CopyAsync_BackupDrive_GetsTheBackupByteForByteInTheFolderNamedForTheRoot()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await rewind.Backups.CreateAsync(ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");

        await DriveFor(rewind).CopyAsync(ct);

        Assert.Equal(
            await File.ReadAllBytesAsync(Path.Combine(rewind.Paths.Root, "backups", Tonight), ct),
            await File.ReadAllBytesAsync(OnDrive(rewind, drive, Tonight), ct));
    }

    // Two Close Outs with the drive at home: both nights' backups go the next time it's plugged in.
    [Fact]
    public async Task CopyAsync_TwoNightsMissed_CopiesBoth()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutBackupAsync(rewind, LastWeek, LastWeeksBackup, ct);
        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");

        await DriveFor(rewind).CopyAsync(ct);

        Assert.Equal([LastWeek, Tonight], DriveFiles(rewind, drive));
        Assert.Equal(LastWeeksBackup, await File.ReadAllBytesAsync(OnDrive(rewind, drive, LastWeek), ct));
    }

    // Its file time, set long ago, stays as it was.
    [Fact]
    public async Task CopyAsync_BackupAlreadyOnTheDrive_DoesntWriteItAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");
        var copy = await PutOnDriveAsync(rewind, drive, Tonight, TonightsBackup, ct);
        File.SetLastWriteTimeUtc(copy, LongAgo);

        await DriveFor(rewind).CopyAsync(ct);

        Assert.Equal(LongAgo, File.GetLastWriteTimeUtc(copy));
    }

    // By name and length, so the drive's backups are never read back: a file of tonight's name and length counts as
    // tonight's backup, whatever its bytes.
    [Fact]
    public async Task CopyAsync_FileOfTheBackupsNameAndLength_CountsAsTheBackup()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");
        var copy = await PutOnDriveAsync(rewind, drive, Tonight, NotTonightsBackup, ct);

        await DriveFor(rewind).CopyAsync(ct);

        Assert.Equal(NotTonightsBackup, await File.ReadAllBytesAsync(copy, ct));
    }

    // Such as a copy made by hand that a full drive cut short. Tonight's is shorter on the drive, and last week's longer.
    [Fact]
    public async Task CopyAsync_FilesOfBackupsNamesAndOtherLengths_AreReplaced()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutBackupAsync(rewind, LastWeek, LastWeeksBackup, ct);
        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");
        var tonights = await PutOnDriveAsync(rewind, drive, Tonight, HalfABackup, ct);
        var lastWeeks = await PutOnDriveAsync(rewind, drive, LastWeek, [.. LastWeeksBackup, .. HalfABackup], ct);

        await DriveFor(rewind).CopyAsync(ct);

        Assert.Equal(TonightsBackup, await File.ReadAllBytesAsync(tonights, ct));
        Assert.Equal(LastWeeksBackup, await File.ReadAllBytesAsync(lastWeeks, ct));
    }

    // The README says to keep the scanner driver's installer with the backups, and SQLite leaves a -wal file beside a
    // backup that's open, such as in the SQLite shell.
    [Fact]
    public async Task CopyAsync_FilesThatArentBackups_AreNotCopied()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        foreach (var other in new[] { "TEEMI-VCOM-Driver-Setup.exe", "notes.txt", "rewind-2026-10-23-2215.db-wal" })
        {
            await PutBackupAsync(rewind, other, HalfABackup, ct);
        }

        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");

        await DriveFor(rewind).CopyAsync(ct);

        Assert.Equal([Tonight], DriveFiles(rewind, drive));
    }

    // What a crash or a power cut partway through last week's backup left on the register: the copy under the name it
    // was being made under, and SQLite's journal beside it.
    [Fact]
    public async Task CopyAsync_UnfinishedBackupOnTheRegister_IsNotCopied()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutBackupAsync(rewind, "rewind-2026-10-16-2215.db.partial", HalfABackup, ct);
        await PutBackupAsync(rewind, "rewind-2026-10-16-2215.db.partial-journal", HalfABackup, ct);
        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");

        await DriveFor(rewind).CopyAsync(ct);

        Assert.Equal([Tonight], DriveFiles(rewind, drive));
    }

    // What a drive pulled out partway through an earlier copy of tonight's backup left: the copy under the name it was
    // being written under. This copy is written under that name too, so it takes that file's place.
    [Fact]
    public async Task CopyAsync_UnfinishedCopyOnTheDrive_IsReplacedNotLeft()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");
        await PutOnDriveAsync(rewind, drive, "rewind-2026-10-23-2215.db.partial", HalfABackup, ct);

        await DriveFor(rewind).CopyAsync(ct);

        Assert.Equal([Tonight], DriveFiles(rewind, drive));
        Assert.Equal(TonightsBackup, await File.ReadAllBytesAsync(OnDrive(rewind, drive, Tonight), ct));
    }

    // F has last week's already, and E has nothing.
    [Fact]
    public async Task CopyAsync_TwoBackupDrives_EachGetsWhatItsMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutBackupAsync(rewind, LastWeek, LastWeeksBackup, ct);
        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        var e = rewind.Drives.PlugInBackupDrive("E");
        var f = rewind.Drives.PlugInBackupDrive("F");
        await PutOnDriveAsync(rewind, f, LastWeek, LastWeeksBackup, ct);

        var copy = await DriveFor(rewind).CopyAsync(ct);

        Assert.Equal([LastWeek, Tonight], DriveFiles(rewind, e));
        Assert.Equal([LastWeek, Tonight], DriveFiles(rewind, f));
        Assert.Equal(3, copy.Files);
    }

    // A copy on the register's own disk isn't a second copy. Drive C stands in for that disk, with the root folder on it,
    // as C:\ holds C:\Rewind, and it has the marker. Taken for a backup drive, it would answer that it has every backup,
    // since its folder for the root is the root's own backups folder.
    [Fact]
    public async Task CopyAsync_RegistersOwnDisk_IsSkippedEvenWithTheMarker()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        var ownDisk = rewind.Drives.PlugInBackupDrive("C");
        var paths = new RewindPaths { Root = Path.Combine(ownDisk, "Rewind") };
        await PutBackupAsync(paths, Tonight, TonightsBackup, ct);

        var copy = await DriveFor(rewind, paths: paths).CopyAsync(ct);

        Assert.Equal(DriveCopyStatus.NoDrive, copy.Status);
    }

    // Windows reads a path the same in any capitals and with either slash, so the drive the root is on is too.
    [Fact]
    public async Task CopyAsync_RegistersOwnDiskWrittenAnotherWay_IsStillSkipped()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows reads a path the same in other capitals.");
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        var ownDisk = rewind.Drives.PlugInBackupDrive("C");
        var paths = new RewindPaths { Root = Path.Combine(ownDisk, "Rewind") };
        await PutBackupAsync(paths, Tonight, TonightsBackup, ct);
        var drives = new FakeDrives(Path.Combine(rewind.Paths.Root, "drives"));
        drives.Add(ownDisk.ToUpperInvariant().Replace('\\', '/'));

        var copy = await DriveFor(rewind, drives: drives, paths: paths).CopyAsync(ct);

        Assert.Equal(DriveCopyStatus.NoDrive, copy.Status);
    }

    // Such as $env:Paths__Root = 'C:\RewindDev\', which is still the RewindDev folder, with the slash doubled as well.
    [Fact]
    public async Task CopyAsync_RootEndingInSlashes_CopiesToTheFolderNamedForIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");
        var paths = new RewindPaths { Root = rewind.Paths.Root + Path.DirectorySeparatorChar + Path.DirectorySeparatorChar };

        await DriveFor(rewind, paths: paths).CopyAsync(ct);

        Assert.Equal([Tonight], DriveFiles(rewind, drive));
    }

    // NET-12: an answer, with how many backups went, and to which drive.
    [Fact]
    public async Task CopyAsync_BackupsCopied_AnswersHowManyAndWhere()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutBackupAsync(rewind, LastWeek, LastWeeksBackup, ct);
        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");

        var copy = await DriveFor(rewind).CopyAsync(ct);

        Assert.Equal((DriveCopyStatus.Copied, 2), (copy.Status, copy.Files));
        Assert.Equal([drive], copy.Drives);
        Assert.Empty(copy.Unwritable);
    }

    // Information (OBS-3): a Close Out step, and where the backups went.
    [Fact]
    public async Task CopyAsync_BackupsCopied_LogsTheDriveAndHowMany()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutBackupAsync(rewind, LastWeek, LastWeeksBackup, ct);
        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");
        var log = new ListLogger<BackupDrive>();

        await DriveFor(rewind, log).CopyAsync(ct);

        Assert.Equal([(LogLevel.Information, $"Copied to the backup drive {drive}: backups 2", (Exception?)null)], log.Entries);
    }

    // Such as a second copy in one night with no backup made in between. Nothing's wrong, so the log has no warning.
    [Fact]
    public async Task CopyAsync_DriveHasEveryBackup_AnswersNothingToCopyWithoutAWarning()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");
        await PutOnDriveAsync(rewind, drive, Tonight, TonightsBackup, ct);
        var log = new ListLogger<BackupDrive>();

        var copy = await DriveFor(rewind, log).CopyAsync(ct);

        Assert.Equal((DriveCopyStatus.NothingToCopy, 0), (copy.Status, copy.Files));
        Assert.Equal([drive], copy.Drives);
        Assert.Equal([LogLevel.Information], log.Entries.Select(entry => entry.Level));
    }

    // GEN-7: the drive was left at home. An answer, not an exception, and one warning in the log.
    [Fact]
    public async Task CopyAsync_NoBackupDrive_AnswersSoAndWarnsOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        var log = new ListLogger<BackupDrive>();

        var copy = await DriveFor(rewind, log).CopyAsync(ct);

        Assert.Equal((DriveCopyStatus.NoDrive, 0), (copy.Status, copy.Files));
        Assert.Empty(copy.Drives);
        Assert.Equal([LogLevel.Warning], log.Entries.Select(entry => entry.Level));
    }

    // GEN-7: a file stands where E's folder for this root goes, so the folder can't be made, on Windows or on Linux, as on
    // a drive that's read-only. An answer that names the drive, not an exception, and one warning in the log.
    [Fact]
    public async Task CopyAsync_DriveCantBeWrittenTo_AnswersSoAndWarnsOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");
        await File.WriteAllTextAsync(Path.Combine(drive, Path.GetFileName(rewind.Paths.Root)), "", ct);
        var log = new ListLogger<BackupDrive>();

        var copy = await DriveFor(rewind, log).CopyAsync(ct);

        Assert.Equal((DriveCopyStatus.Unwritable, 0), (copy.Status, copy.Files));
        Assert.Equal([drive], copy.Unwritable);
        Assert.Equal([LogLevel.Warning], log.Entries.Select(entry => entry.Level));
    }

    // A folder stands where tonight's backup goes, so its copy is written whole under another name, then can't be
    // renamed into place. That file is deleted, so the drive holds nothing with a backup's name, or the other.
    [Fact]
    public async Task CopyAsync_CopyCantTakeItsPlace_LeavesNoFileBehind()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");
        Directory.CreateDirectory(OnDrive(rewind, drive, Tonight));

        var copy = await DriveFor(rewind).CopyAsync(ct);

        Assert.Equal(DriveCopyStatus.Unwritable, copy.Status);
        Assert.Empty(DriveFiles(rewind, drive));
    }

    // Tonight's backup goes first, so a drive that fills up partway still has it. A folder stands where last week's goes.
    [Fact]
    public async Task CopyAsync_DriveFailsPartway_HasTonightsBackup()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutBackupAsync(rewind, LastWeek, LastWeeksBackup, ct);
        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");
        Directory.CreateDirectory(OnDrive(rewind, drive, LastWeek));

        var copy = await DriveFor(rewind).CopyAsync(ct);

        Assert.Equal([Tonight], DriveFiles(rewind, drive));
        Assert.Equal((DriveCopyStatus.Unwritable, 1), (copy.Status, copy.Files));
    }

    // A drive that's full, read-only or gone would fail every write the same way, so the first that fails is the last
    // tried on that drive. A folder stands where tonight's goes.
    [Fact]
    public async Task CopyAsync_FirstWriteFails_TriesNoMoreOnThatDrive()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutBackupAsync(rewind, LastWeek, LastWeeksBackup, ct);
        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");
        Directory.CreateDirectory(OnDrive(rewind, drive, Tonight));

        var copy = await DriveFor(rewind).CopyAsync(ct);

        Assert.Empty(DriveFiles(rewind, drive));
        Assert.Equal(0, copy.Files);
    }

    // The register's own disk failing is no fault of the drive's, so it isn't answered as one, and Close Out reports it as
    // it would any other failure. Another program has tonight's backup open, and lets nobody else read it.
    [Fact]
    public async Task CopyAsync_BackupTheRegisterCantRead_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        rewind.Drives.PlugInBackupDrive("E");
        await using var held = new FileStream(Path.Combine(rewind.Paths.Root, "backups", Tonight), FileMode.Open, FileAccess.Read, FileShare.None);

        await Assert.ThrowsAnyAsync<IOException>(() => DriveFor(rewind).CopyAsync(ct));
    }

    // Before the first backup, such as when tonight's couldn't be made, the drive is left as it was: its folder for the
    // root is made only once there's a backup to put in it.
    [Fact]
    public async Task CopyAsync_NoBackupsYet_LeavesTheDriveAsItWas()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        var drive = rewind.Drives.PlugInBackupDrive("E");

        var copy = await DriveFor(rewind).CopyAsync(ct);

        Assert.Equal(DriveCopyStatus.NothingToCopy, copy.Status);
        Assert.Equal(["REWIND-BACKUP.txt"], Directory.EnumerateFileSystemEntries(drive).Select(entry => Path.GetFileName(entry)));
    }

    // GEN-7: E can't be written to, as above, and F still gets tonight's backup.
    [Fact]
    public async Task CopyAsync_OneDriveCantBeWrittenTo_StillCopiesToTheOther()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        var e = rewind.Drives.PlugInBackupDrive("E");
        await File.WriteAllTextAsync(Path.Combine(e, Path.GetFileName(rewind.Paths.Root)), "", ct);
        var f = rewind.Drives.PlugInBackupDrive("F");

        var copy = await DriveFor(rewind).CopyAsync(ct);

        Assert.Equal([Tonight], DriveFiles(rewind, f));
        Assert.Equal((DriveCopyStatus.Unwritable, 1), (copy.Status, copy.Files));
        Assert.Equal([e, f], copy.Drives);
        Assert.Equal([e], copy.Unwritable);
    }

    // NET-7: the drive has every backup, so only a call that hears the cancel before it starts can throw.
    [Fact]
    public async Task CopyAsync_CancelledBeforeItStarts_Throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");
        await PutOnDriveAsync(rewind, drive, Tonight, TonightsBackup, ct);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DriveFor(rewind).CopyAsync(cancelled.Token));
    }

    // NET-7: cancelled as the drives are listed, once the call has started, so the reads and writes after are what have to
    // hear it.
    [Fact]
    public async Task CopyAsync_CancelledOnceItsStarted_CopiesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await PutBackupAsync(rewind, LastWeek, LastWeeksBackup, ct);
        await PutBackupAsync(rewind, Tonight, TonightsBackup, ct);
        var drive = rewind.Drives.PlugInBackupDrive("E");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DriveFor(rewind, drives: new CancelAsListed(rewind.Drives, cancel)).CopyAsync(cancel.Token));

        Assert.Empty(DriveFiles(rewind, drive));
    }

    // Close Out starts on the screens' thread, and a USB hard drive can take seconds to spin up when it's asked whether
    // it's ready, so the call hands back before it looks at a drive.
    [Fact]
    public async Task CopyAsync_DriveSpinningUp_HandsBackBeforeItAnswers()
    {
        await using var rewind = new TempRewind();
        using var drives = new SpinningUp();

        var copying = DriveFor(rewind, drives: drives).CopyAsync(TestContext.Current.CancellationToken);
        var handedBack = !copying.IsCompleted;
        drives.Answer();
        await copying;

        Assert.True(handedBack);
    }

    // The copy for the test's root folder and drives, unless the test gives others.
    private static BackupDrive DriveFor(
        TempRewind rewind,
        ILogger<BackupDrive>? logger = null,
        IDrives? drives = null,
        RewindPaths? paths = null)
    {
        var options = Options.Create(paths ?? rewind.Paths);
        return new(
            new Backups(options, rewind.Time, NullLogger<Backups>.Instance),
            drives ?? rewind.Drives,
            options,
            logger ?? NullLogger<BackupDrive>.Instance);
    }

    // A file in the register's backups folder, written straight there as a stand-in.
    private static Task PutBackupAsync(TempRewind rewind, string name, byte[] bytes, CancellationToken cancellationToken) =>
        PutBackupAsync(rewind.Paths, name, bytes, cancellationToken);

    private static async Task PutBackupAsync(RewindPaths paths, string name, byte[] bytes, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(paths.Root, "backups");
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, name), bytes, cancellationToken);
    }

    // A file already on the drive, in its folder for this root.
    private static async Task<string> PutOnDriveAsync(TempRewind rewind, string drive, string name, byte[] bytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(OnDrive(rewind, drive));
        var path = OnDrive(rewind, drive, name);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        return path;
    }

    // The drive's folder for this root, or a path in it: E:\<the root folder's name>\backups, written out here rather
    // than taken from RewindPaths.
    private static string OnDrive(TempRewind rewind, string drive, params string[] path) =>
        Path.Combine([drive, Path.GetFileName(rewind.Paths.Root), "backups", .. path]);

    // The name of each file in the drive's folder for this root, in order by character code. Empty when there's no folder.
    private static List<string> DriveFiles(TempRewind rewind, string drive)
    {
        var folder = OnDrive(rewind, drive);
        return Directory.Exists(folder)
            ? [.. Directory.GetFiles(folder).Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal)]
            : [];
    }

    // Drives that answer only once the test says so, as a USB hard drive answers once it has spun up.
    private sealed class SpinningUp : IDrives, IDisposable
    {
        private readonly ManualResetEventSlim _spunUp = new();

        public IReadOnlyList<string> TopFolders()
        {
            // Real time, not fake: a call that waits here on the test's own thread fails the test after 10 seconds
            // instead of hanging the run.
            _spunUp.Wait(TimeSpan.FromSeconds(10));
            return [];
        }

        public void Answer() => _spunUp.Set();

        public void Dispose() => _spunUp.Dispose();
    }

    // The test's drives, which cancel the call as they're listed.
    private sealed class CancelAsListed(IDrives drives, CancellationTokenSource cancel) : IDrives
    {
        public IReadOnlyList<string> TopFolders()
        {
            cancel.Cancel();
            return drives.TopFolders();
        }
    }
}
