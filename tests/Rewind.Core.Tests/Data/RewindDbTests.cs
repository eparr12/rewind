using Microsoft.EntityFrameworkCore;

namespace Rewind.Core.Tests.Data;

public sealed class RewindDbTests
{
    [Fact]
    public async Task Open_AnyConnection_HasForeignKeysOn()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var rewind = new TempRewind();
        await rewind.StartAsync(ct);
        await using var db = rewind.OpenDb();

        var foreignKeys = await db.Database
            .SqlQueryRaw<bool>("SELECT foreign_keys AS Value FROM pragma_foreign_keys")
            .SingleAsync(ct);

        Assert.True(foreignKeys);
    }

    [Fact]
    public async Task Model_ComparedWithMigrations_HasNoPendingChanges()
    {
        await using var rewind = new TempRewind();
        await using var db = rewind.OpenDb();

        // A model change without a migration fails here, before it can fail on the register.
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
