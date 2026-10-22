using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Rewind.Core.Data;

/// <summary>
/// Copies the live database with SQLite's backup API, which is safe while Rewind runs and
/// includes changes still waiting in rewind.db-wal (DATA-14).
/// </summary>
public sealed class Backups(IOptions<RewindPaths> options, TimeProvider time)
{
    private const string StampFormat = "yyyy-MM-dd-HHmm";

    private readonly RewindPaths _paths = options.Value;

    /// <summary>Whether the database file exists yet. A first start has nothing to back up.</summary>
    public bool HaveDatabase => File.Exists(_paths.Database);

    /// <summary>
    /// Copies the database to a new file named for the local time, such as
    /// rewind-2026-10-23-2215.db, and returns its path.
    /// </summary>
    public Task<string> CreateAsync(CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                Directory.CreateDirectory(_paths.Backups);
                var path = NewBackupPath();
                using var live = new SqliteConnection(RewindDb.ConnectionStringFor(_paths.Database));
                using var copy = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Pooling = false, // releases the file as soon as the copy is done
                }.ToString());
                live.Open();
                live.BackupDatabase(copy); // blocks until the copy is complete
                return path;
            },
            cancellationToken);

    // A second backup in the same minute gets -2, -3 and so on, so a backup is never overwritten.
    private string NewBackupPath()
    {
        var stamp = time.GetLocalNow().ToString(StampFormat, CultureInfo.InvariantCulture);
        var path = Path.Combine(_paths.Backups, $"rewind-{stamp}.db");
        for (var copy = 2; File.Exists(path); copy++)
        {
            path = Path.Combine(_paths.Backups, $"rewind-{stamp}-{copy}.db");
        }

        return path;
    }
}
