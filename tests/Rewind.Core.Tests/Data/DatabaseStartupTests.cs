using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Rewind.Core.Tests.Data;

public sealed class DatabaseStartupTests
{
    [Fact]
    public async Task StartAsync_FirstStart_CreatesDatabaseWithoutBackup()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();

        await rewind.StartAsync(ct);

        Assert.True(File.Exists(rewind.Paths.Database));
        Assert.Empty(rewind.BackupFiles());
    }

    [Fact]
    public async Task StartAsync_DatabaseNotInWalMode_TurnsWalOn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using (var touched = rewind.OpenDb())
        {
            // Like a database file another tool has switched out of WAL mode.
            await touched.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = DELETE;", ct);
        }

        await rewind.StartAsync(ct);

        await using var db = rewind.OpenDb();
        var mode = await db.Database
            .SqlQueryRaw<string>("SELECT journal_mode AS Value FROM pragma_journal_mode")
            .SingleAsync(ct);
        Assert.Equal("wal", mode);
    }

    [Fact]
    public async Task StartAsync_MigrationPending_BacksUpBeforeMigrating()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await CreateDatabaseBeforeMigrationAsync(rewind, ct);

        await rewind.StartAsync(ct);

        // The backup is the database as it was before: no migrations applied.
        var backup = Assert.Single(rewind.BackupFiles());
        await using var copy = rewind.OpenDb(backup);
        Assert.Empty(await copy.Database.GetAppliedMigrationsAsync(ct));
    }

    [Fact]
    public async Task StartAsync_BackupFails_LeavesMigrationPending()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await CreateDatabaseBeforeMigrationAsync(rewind, ct);
        await File.WriteAllTextAsync(rewind.Paths.Backups, "", ct); // a file where the folder goes

        await Assert.ThrowsAnyAsync<IOException>(() => rewind.StartAsync(ct));

        await using var db = rewind.OpenDb();
        Assert.NotEmpty(await db.Database.GetPendingMigrationsAsync(ct));
    }

    [Fact]
    public async Task StartAsync_UpToDate_MakesNoBackup()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);

        await rewind.StartAsync(ct);

        Assert.Empty(rewind.BackupFiles());
    }

    [Fact(Timeout = 30_000)] // a start that waits for the lock never finishes
    public async Task StartAsync_LeftoverLockAndUpToDate_StartsWithoutWaiting()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await LeaveMigrationLockAsync(rewind, ct);

        var error = await Record.ExceptionAsync(() => rewind.StartAsync(ct));

        Assert.Null(error);
    }

    [Fact(Timeout = 30_000)] // a start that waits for the lock never finishes
    public async Task StartAsync_LeftoverLockAndMigrationPending_GivesUpWaiting()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await CreateDatabaseBeforeMigrationAsync(rewind, ct);
        await LeaveMigrationLockAsync(rewind, ct);

        var start = rewind.StartAsync(ct);
        rewind.Time.Advance(await rewind.Time.NextTimerAsync(ct)); // the migration's time limit

        await Assert.ThrowsAsync<TimeoutException>(() => start);
    }

    // A database from before the Initial migration: it exists, so Rewind backs it up, and
    // every migration is still pending.
    private static async Task CreateDatabaseBeforeMigrationAsync(TempRewind rewind, CancellationToken ct)
    {
        Directory.CreateDirectory(rewind.Paths.Data);
        await using var db = rewind.OpenDb();
        await db.Database.MigrateAsync(Migration.InitialDatabase, ct);
    }

    // A crash in the middle of a migration leaves EF Core's lock row behind.
    private static async Task LeaveMigrationLockAsync(TempRewind rewind, CancellationToken ct)
    {
        await using var db = rewind.OpenDb();
        await db.Database.ExecuteSqlRawAsync(
            """INSERT INTO "__EFMigrationsLock" ("Id", "Timestamp") VALUES (1, '2026-10-23 22:15:00');""",
            ct);
    }
}
