using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Rewind.Core.Data;

namespace Rewind.Core.Tests;

/// <summary>
/// Contexts that run something just after each save is in, for a test of what a service does once its write has
/// landed, such as a clerk who leaves the screen just then. A cancel from here on must not turn the done outcome into
/// an exception (NET-7). EF Core saves one row in one statement with no transaction, as closing the night does, so
/// <see cref="AfterEachCommit"/> never hears that save, and this does.
/// </summary>
internal sealed class AfterEachSave(TempRewind rewind, Func<CancellationToken, Task> after) : IDbContextFactory<RewindDb>
{
    public RewindDb CreateDbContext() =>
        new(new DbContextOptionsBuilder<RewindDb>()
            .UseSqlite(RewindDb.ConnectionStringFor(rewind.Paths.Database))
            .AddInterceptors(new Interceptor(after))
            .Options);

    private sealed class Interceptor(Func<CancellationToken, Task> after) : SaveChangesInterceptor
    {
        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            await after(cancellationToken);
            return result;
        }
    }
}
