using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rewind.Core.Data;
using Rewind.Core.Devices;

namespace Rewind.Core.CloseOut;

/// <summary>
/// Copies the register's backups to the USB backup drive at Close Out (Build Plan 3.5), so the family's data has a
/// second home off the register. The backup drive is any drive with REWIND-BACKUP.txt in its top folder, so Rewind
/// never writes to a random flash drive. The register's own disk is never one, marker or not, since a copy there isn't
/// a second copy. The backup drive keeps every backup: nothing on it is ever deleted, apart from an unfinished copy of
/// Rewind's own, and a file with a backup's name is only ever replaced by that backup.
/// </summary>
public sealed partial class BackupDrive(
    Backups backups,
    IDrives drives,
    IOptions<RewindPaths> options,
    ILogger<BackupDrive> logger)
{
    /// <summary>The file in a drive's top folder that makes it a backup drive.</summary>
    public const string MarkerFileName = "REWIND-BACKUP.txt";

    private readonly RewindPaths _paths = options.Value;

    /// <summary>
    /// Copies each backup a backup drive doesn't have yet to its folder for the root folder: E:\Rewind\backups for the
    /// register's C:\Rewind, and E:\RewindDev\backups for a test folder such as C:\RewindDev, so a test's backups
    /// never sit among the store's. A drive has a backup when it has a file of its name and length, so one made on a
    /// night the drive wasn't plugged in goes the next time it is, and a file of its name and another length is
    /// replaced. Each copy is written whole under another name and then renamed, so a drive pulled out partway
    /// through holds nothing that looks like a backup. With more than one backup drive, each gets what it's missing.
    /// A missing drive, or one that can't be written to, is an answer, not an exception (NET-12, GEN-7), and the log
    /// has a warning. A cancel throws.
    /// </summary>
    public Task<DriveCopy> CopyAsync(CancellationToken cancellationToken) =>
        // On the thread pool: a USB hard drive that's asked whether it's ready can take seconds to spin up, and the
        // screens' thread would stop with it.
        Task.Run(() => CopyToEachAsync(cancellationToken), cancellationToken);

    private async Task<DriveCopy> CopyToEachAsync(CancellationToken cancellationToken)
    {
        var found = FindBackupDrives();
        if (found.Count == 0)
        {
            LogNoDrive(logger, MarkerFileName);
            return new(DriveCopyStatus.NoDrive, 0, [], []);
        }

        // Newest first: tonight's backup matters most, so a drive that fills up or is pulled out partway has it.
        var newestFirst = backups.List().Reverse().ToList();
        var files = 0;
        var unwritable = new List<string>();
        foreach (var drive in found)
        {
            var (copied, failure) = await CopyMissingAsync(drive, newestFirst, cancellationToken);
            files += copied;
            if (failure is not null)
            {
                unwritable.Add(drive);
            }

            LogOutcome(drive, copied, failure);
        }

        var status = unwritable.Count > 0 ? DriveCopyStatus.Unwritable
            : files > 0 ? DriveCopyStatus.Copied
            : DriveCopyStatus.NothingToCopy;
        return new(status, files, found, unwritable);
    }

    // The register's own disk is left out before anything on it is looked at.
    private List<string> FindBackupDrives() =>
        [.. drives.TopFolders()
            .Where(drive => !HoldsTheRoot(drive))
            .Where(drive => File.Exists(Path.Combine(drive, MarkerFileName)))];

    // Each backup the drive is missing, newest first, up to the first write that fails: a drive that's full, read-only
    // or gone would fail the rest the same way. Returns how many it copied, and the failure if there was one.
    private async Task<(int Copied, Exception? Failure)> CopyMissingAsync(
        string drive,
        List<string> newestFirst,
        CancellationToken cancellationToken)
    {
        var folder = FolderOn(drive);
        var copied = 0;
        foreach (var backup in newestFirst)
        {
            var copy = Path.Combine(folder, Path.GetFileName(backup));
            if (IsOnDrive(backup, copy))
            {
                continue;
            }

            // Read before the try: a backup the register's own disk can't read is no fault of the drive's, so it throws.
            var bytes = await File.ReadAllBytesAsync(backup, cancellationToken);
            try
            {
                Directory.CreateDirectory(folder); // only once there's something to put in it
                await WholeFile.WriteAsync(copy, bytes, cancellationToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return (copied, e);
            }

            copied++;
        }

        return (copied, null);
    }

    // Whether the root folder is on this drive, as C:\Rewind is on C:\. A drive's top folder ends in a separator, so
    // only whole folder names match. Either slash, and any capitals, since Windows reads a path written either way.
    private bool HoldsTheRoot(string drive) =>
        Path.GetFullPath(_paths.Root).StartsWith(Path.GetFullPath(drive), StringComparison.OrdinalIgnoreCase);

    // The root folder's own name, then the backups folder's, as on the register: E:\Rewind\backups for C:\Rewind, and
    // for C:\Rewind\ too.
    private string FolderOn(string drive) =>
        Path.Combine(
            drive,
            Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(_paths.Root))),
            Path.GetFileName(_paths.Backups));

    private void LogOutcome(string drive, int copied, Exception? failure)
    {
        if (copied > 0)
        {
            LogCopied(logger, drive, copied);
        }

        if (failure is not null)
        {
            LogUnwritable(logger, failure, drive);
        }
        else if (copied == 0)
        {
            LogHadEvery(logger, drive);
        }
    }

    // The drive has the backup when it has a file of the backup's name and length. A drive that's been pulled out
    // answers that it hasn't, rather than throwing.
    private static bool IsOnDrive(string backup, string copy)
    {
        var onDrive = new FileInfo(copy);
        return onDrive.Exists && onDrive.Length == new FileInfo(backup).Length;
    }

    // Information (OBS-3): a Close Out step, and where the backups went.
    [LoggerMessage(Level = LogLevel.Information, Message = "Copied to the backup drive {Drive}: backups {Copied}")]
    private static partial void LogCopied(ILogger logger, string drive, int copied);

    [LoggerMessage(Level = LogLevel.Information, Message = "The backup drive {Drive} had every backup already")]
    private static partial void LogHadEvery(ILogger logger, string drive);

    // Warnings (OBS-3): handled, and Close Out goes on, but the backups have no second copy until a drive takes them.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Found no backup drive: no drive but the register's own has {Marker} in its top folder")]
    private static partial void LogNoDrive(ILogger logger, string marker);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't write to the backup drive {Drive}, which may be full, read-only or pulled out, so it may still be missing backups")]
    private static partial void LogUnwritable(ILogger logger, Exception exception, string drive);
}
