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
    ILogger<DatabaseStartup> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.Value.Data); // SQLite creates the file, but not its folder

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken);
        if (pending.Any())
        {
            if (backups.HaveDatabase) // a first start has nothing to back up
            {
                var backup = await backups.CreateAsync(cancellationToken);
                LogBackedUp(logger, backup);
            }

            // Migrate only when something is pending: EF Core takes a lock to migrate, and a
            // crash can leave it behind. Clear one with: DELETE FROM "__EFMigrationsLock";
            await db.Database.MigrateAsync(cancellationToken);
        }

        // EF Core turns WAL on when it creates the file. A database restored from a backup
        // may not have it, and the setting is saved in the file, so check every start.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;", cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // OBS-1 with the build's analyzers: CA1848 asks for a source-generated method per message.
    [LoggerMessage(Level = LogLevel.Information, Message = "Backed up the database to {Backup} before migrating")]
    private static partial void LogBackedUp(ILogger logger, string backup);
}
