using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Rewind.Core.Data;

/// <summary>
/// The register's backups of the database, in the backups folder. Each is a copy made with SQLite's backup API, which
/// is safe while Rewind runs and includes changes still waiting in rewind.db-wal (DATA-14). A backup is a file with a
/// name <see cref="CreateAsync"/> gives, such as rewind-2026-10-23-2215.db. Nothing else in the folder is ever listed
/// or counted, such as the scanner driver's installer, which the README says to keep there. Nor is anything else
/// deleted, apart from the unfinished file of a backup that fails, which is that backup's own.
/// </summary>
public sealed partial class Backups(IOptions<RewindPaths> options, TimeProvider time, ILogger<Backups> logger) : ILastBackup
{
    /// <summary>How many backups the register keeps (GEN-4). The backup drive keeps every one.</summary>
    public const int Kept = 30;

    private const string StampFormat = "yyyy-MM-dd-HHmm";

    private readonly RewindPaths _paths = options.Value;

    private bool DatabaseExists => File.Exists(_paths.Database);

    /// <inheritdoc />
    public event Action? BackedUp;

    /// <summary>
    /// Copies the database to a new file named for the local time, such as rewind-2026-10-23-2215.db, and returns its
    /// path, or null when there's no database yet, as on a first start. The copy is made under a name that isn't a
    /// backup's, and renamed once it's whole and its file is closed, and then <see cref="BackedUp"/> is raised. A
    /// listener that throws is logged as an error, and neither fails the backup nor keeps the news from the others. A
    /// backup that fails deletes that file and throws, so it leaves nothing behind, and raises nothing. One that a
    /// crash or a power cut stops partway leaves the file, which is never taken for a backup: it stays as it is, and the
    /// next backup takes another name.
    /// </summary>
    public Task<string?> CreateAsync(CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                // Opening a missing file would create an empty database, and back that up.
                if (!DatabaseExists)
                {
                    return null;
                }

                Directory.CreateDirectory(_paths.Backups);
                var path = NewBackupPath();
                var partial = path + WholeFile.PartialExtension;
                try
                {
                    CopyDatabaseTo(partial);
                    File.Move(partial, path); // never over another file: NewBackupPath picked a name nothing has
                }
                finally
                {
                    // SQLite makes the file as it opens it, before it reads the database, so a backup that fails
                    // can leave it. It's this backup's own. Gone already after the move; this does nothing then.
                    File.Delete(partial);
                }

                RaiseBackedUp(path);
                return path;
            },
            cancellationToken);

    /// <summary>
    /// The backups, oldest first, as paths: in order of the time in each one's name, then its number, so
    /// rewind-2026-10-23-2215.db comes before rewind-2026-10-23-2215-2.db, and that before rewind-2026-10-23-2215-10.db.
    /// Only a file with a name <see cref="CreateAsync"/> gives is listed, so a backup a crash left unfinished isn't,
    /// and neither is the -wal file SQLite keeps beside a backup it has open. Empty before the first backup.
    /// </summary>
    public IReadOnlyList<string> List() => [.. InOrder().Select(backup => backup.File)];

    /// <inheritdoc />
    public DateTime? NewestMadeAt()
    {
        try
        {
            return InOrder() is [.., var newest] ? newest.Name.Time : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogNotRead(logger, e, _paths.Backups);
            return null;
        }
    }

    /// <summary>
    /// Deletes the oldest backups past the newest <see cref="Kept"/>, and returns how many went. A backup it can't
    /// delete, such as one another program has open, is logged as a warning and left for next time, and the rest are
    /// still deleted.
    /// </summary>
    public int KeepNewest()
    {
        var deleted = 0;
        foreach (var backup in List().SkipLast(Kept))
        {
            if (TryDelete(backup))
            {
                deleted++;
            }
        }

        return deleted;
    }

    // A second backup in the same minute gets -2, -3 and so on, so a backup is never overwritten. So does one whose
    // name an unfinished backup still holds, which a crash or a power cut left, so that's never written over either.
    // No file has the name picked, or that name with .partial, so the unfinished file a backup that fails deletes is
    // always that backup's own.
    private string NewBackupPath()
    {
        var stamp = time.GetLocalNow().ToString(StampFormat, CultureInfo.InvariantCulture);
        var path = Path.Combine(_paths.Backups, $"rewind-{stamp}.db");
        for (var copy = 2; File.Exists(path) || File.Exists(path + WholeFile.PartialExtension); copy++)
        {
            path = Path.Combine(_paths.Backups, $"rewind-{stamp}-{copy}.db");
        }

        return path;
    }

    // Both connections are closed when this returns or throws, the copy's for good, since it isn't pooled, so its file
    // can be renamed, or deleted.
    private void CopyDatabaseTo(string file)
    {
        using var live = new SqliteConnection(RewindDb.ConnectionStringFor(_paths.Database));
        using var copy = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = file,
            Pooling = false, // releases the file as soon as the copy is done
        }.ToString());
        live.Open();
        live.BackupDatabase(copy); // blocks until the copy is complete
    }

    // Each listener on its own, as ScannerHub does. The backup is made by now, and a done outcome stays done (NET-7's
    // reasoning), so a listener that throws can't fail it: at Close Out the checklist would say it wasn't saved, and
    // before a migration Rewind wouldn't start.
    private void RaiseBackedUp(string backup)
    {
        foreach (var listener in Delegate.EnumerateInvocationList(BackedUp))
        {
            try
            {
                listener();
            }
            catch (Exception e) // GEN-7: the backup is made, whatever a listener does with the news
            {
                LogListenerFailed(logger, e, backup);
            }
        }
    }

    // The backups, oldest first, each with the time and number in its name, as List gives them.
    private List<(string File, BackupName Name)> InOrder()
    {
        if (!Directory.Exists(_paths.Backups))
        {
            return [];
        }

        var backups = new List<(string File, BackupName Name)>();
        foreach (var file in Directory.EnumerateFiles(_paths.Backups))
        {
            if (TryParseName(Path.GetFileName(file), out var name))
            {
                backups.Add((file, name));
            }
        }

        return [.. backups.OrderBy(backup => backup.Name.Time).ThenBy(backup => backup.Name.Number)];
    }

    private bool TryDelete(string backup)
    {
        try
        {
            File.Delete(backup);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogNotDeleted(logger, e, backup);
            return false;
        }

        LogDeleted(logger, backup, Kept);
        return true;
    }

    // The time and number in a name CreateAsync gives: rewind-yyyy-MM-dd-HHmm.db for a minute's first backup, which is
    // number 1, and the same with -2, -3 and so on before .db for the others. A name whose time can't be, such as one in
    // month 13, isn't a backup's.
    private static bool TryParseName(string fileName, out BackupName name)
    {
        name = default;
        var match = NamePattern().Match(fileName);
        if (!match.Success
            || !DateTime.TryParseExact(match.Groups["stamp"].Value, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp))
        {
            return false;
        }

        var number = match.Groups["number"];
        name = new(stamp, number.Success ? int.Parse(number.Value, CultureInfo.InvariantCulture) : 1);
        return true;
    }

    // CreateAsync's numbers start at 2 and have no leading 0. Nine digits at most, so one always fits in an int.
    [GeneratedRegex("^rewind-(?<stamp>[0-9]{4}-[0-9]{2}-[0-9]{2}-[0-9]{4})(?:-(?<number>[2-9]|[1-9][0-9]{1,8}))?\\.db$")]
    private static partial Regex NamePattern();

    // Information (OBS-3): a backup is the family's history, so the log says which went.
    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted the backup {Backup}, the oldest past the newest {Kept}")]
    private static partial void LogDeleted(ILogger logger, string backup, int kept);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't delete the old backup {Backup}, such as when another program has it open; it's tried again next time")]
    private static partial void LogNotDeleted(ILogger logger, Exception exception, string backup);

    // Warning (OBS-3): handled, since the read answers no backup, but the folder may still hold some.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't read the backups folder {Folder}, such as after its permissions were changed by hand, so the status bar says there's no backup yet, though there may be some in it")]
    private static partial void LogNotRead(ILogger logger, Exception exception, string folder);

    // Error (OBS-3): a listener with a bug needs looking at, though the backup is made.
    [LoggerMessage(Level = LogLevel.Error, Message = "A listener threw on hearing of the new backup {Backup}; the backup is made all the same")]
    private static partial void LogListenerFailed(ILogger logger, Exception exception, string backup);

    // The local time in a backup's name, and its number within that minute.
    private readonly record struct BackupName(DateTime Time, int Number);
}
