using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Rewind.Core.Data;

/// <summary>
/// Gets the database ready before anything else starts: it backs up the database when a
/// migration is pending, applies the migrations, then makes sure WAL mode is on (DATA-1, DATA-5).
/// </summary>
public sealed partial class DatabaseStartup(
    IDbContextFactory<RewindDb> dbFactory,
    Backups backups,
    IOptions<RewindPaths> paths,
    TimeProvider time,
    ILogger<DatabaseStartup> logger) : IHostedService
{
    // Migrating the family's database takes seconds, so a wait this long means something
    // else holds EF Core's migration lock, or a crash left it behind.
    private static readonly TimeSpan MigrationTimeout = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.Value.Data); // SQLite creates the file, but not its folder

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
        {
            var backup = await backups.CreateAsync(cancellationToken);
            if (backup is not null)
            {
                LogBackedUp(logger, backup);
            }

            await MigrateAsync(db, cancellationToken);
            LogMigrated(logger, pending);
        }

        // EF Core turns WAL on when it creates the file, and Rewind's backups keep it, because
        // the backup API copies the header that records it. A file another tool has touched may
        // not be in WAL mode, and the mode is saved in the file, so check every start.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // EF Core takes a lock to migrate and waits as long as the lock is held. Without a limit,
    // a lock left by a crash would hang every start before the window opens (GEN-7). With one,
    // the start fails, and the log says how to clear the lock (OBS-5).
    private async Task MigrateAsync(RewindDb db, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(MigrationTimeout, time);
        using var migration = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await db.Database.MigrateAsync(migration.Token);
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                "EF Core's migration lock was still held after a minute. If no other copy of Rewind is running, " +
                "a crash during a migration left it behind: see \"If a migration was interrupted\" in the README.",
                exception);
        }
    }

    // OBS-1 with the build's analyzers: CA1848 asks for a source-generated method per message.
    [LoggerMessage(Level = LogLevel.Information, Message = "Backed up the database to {Backup} before migrating")]
    private static partial void LogBackedUp(ILogger logger, string backup);

    // EF Core's own lines are below the Warning level the log keeps for Microsoft's libraries.
    [LoggerMessage(Level = LogLevel.Information, Message = "Applied the migrations {Migrations}")]
    private static partial void LogMigrated(ILogger logger, IReadOnlyList<string> migrations);
}
