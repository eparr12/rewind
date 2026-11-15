using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Rewind.Core.Data;

namespace Rewind.Core.Tests;

/// <summary>
/// Contexts whose connections read backwards wherever a query leaves the order to SQLite, for a test that a
/// result doesn't rest on an order SQLite never promised (TEST-4). SQLite's reverse_unordered_selects pragma is
/// there for finding those. A pragma lasts as long as its connection, so these connections aren't pooled, and
/// none goes back to the pool the other contexts draw from.
/// </summary>
internal sealed class BackwardsReads(TempRewind rewind) : IDbContextFactory<RewindDb>
{
    public RewindDb CreateDbContext() =>
        new(new DbContextOptionsBuilder<RewindDb>()
            .UseSqlite(new SqliteConnectionStringBuilder(RewindDb.ConnectionStringFor(rewind.Paths.Database)) { Pooling = false }.ToString())
            .AddInterceptors(new Interceptor())
            .Options);

    private sealed class Interceptor : DbConnectionInterceptor
    {
        public override async Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA reverse_unordered_selects = 1";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
