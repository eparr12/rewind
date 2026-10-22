using Microsoft.EntityFrameworkCore;

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
        await using (var restored = rewind.OpenDb())
        {
            // Like a backup restored from a copy that wasn't in WAL mode.
            await restored.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = DELETE;", ct);
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
        Directory.CreateDirectory(rewind.Paths.Data);
        string firstMigration;
        await using (var db = rewind.OpenDb())
        {
            firstMigration = db.Database.GetMigrations().First();
            await db.Database.MigrateAsync(firstMigration, ct);
        }

        await rewind.StartAsync(ct);

        // The backup is the database as it was before: only the first migration applied.
        var backup = Assert.Single(rewind.BackupFiles());
        await using var copy = rewind.OpenDb(backup);
        var applied = await copy.Database.GetAppliedMigrationsAsync(ct);
        Assert.Equal(firstMigration, Assert.Single(applied));
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
    public async Task StartAsync_LeftoverMigrationLock_StartsWithoutWaiting()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using (var db = rewind.OpenDb())
        {
            // A crash in the middle of a migration leaves EF Core's lock row behind.
            await db.Database.ExecuteSqlRawAsync(
                """INSERT INTO "__EFMigrationsLock" ("Id", "Timestamp") VALUES (1, '2026-10-23 22:15:00');""",
                ct);
        }

        var error = await Record.ExceptionAsync(() => rewind.StartAsync(ct));

        Assert.Null(error);
    }
}
