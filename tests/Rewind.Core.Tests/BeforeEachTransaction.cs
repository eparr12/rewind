using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Rewind.Core.Data;

namespace Rewind.Core.Tests;

/// <summary>
/// Contexts that run something just before each explicit transaction begins, for a test of what a service does
/// when something gets in before it. That's the only place another write can get in. EF Core begins a
/// transaction on SQLite as BEGIN IMMEDIATE, so once it's open it holds the write lock, and a write from a second
/// connection through <see cref="BeforeEachSave"/> would wait out the 30-second timeout and fail. It also catches
/// a service that reads before its transaction begins, which then saves over what got in.
/// </summary>
internal sealed class BeforeEachTransaction(TempRewind rewind, Func<CancellationToken, Task> before) : IDbContextFactory<RewindDb>
{
    public RewindDb CreateDbContext() =>
        new(new DbContextOptionsBuilder<RewindDb>()
            .UseSqlite(RewindDb.ConnectionStringFor(rewind.Paths.Database))
            .AddInterceptors(new Interceptor(before))
            .Options);

    private sealed class Interceptor(Func<CancellationToken, Task> before) : DbTransactionInterceptor
    {
        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            await before(cancellationToken);
            return result;
        }
    }
}
