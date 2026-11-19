using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rewind.Core.Tests;

/// <summary>
/// Makes connections read backwards wherever a query leaves the order to SQLite, for a test that a
/// result doesn't rest on an order SQLite never promised (TEST-4). SQLite's reverse_unordered_selects pragma is
/// there for finding those. It's set on connections opened asynchronously, as Core opens all of its own, so a
/// connection opened synchronously reads in the usual order. A pragma lasts as long as its connection, so these
/// connections aren't pooled, and none goes back to the pool the other contexts draw from: a test asks for its
/// contexts with <c>pooled: false</c>.
/// </summary>
internal sealed class BackwardsReads : DbConnectionInterceptor
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
