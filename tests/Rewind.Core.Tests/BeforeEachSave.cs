using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Rewind.Core.Data;

namespace Rewind.Core.Tests;

/// <summary>
/// Contexts that run something just before each save, for a test of what a service does when something
/// gets in first, such as another save that a unique index then holds against this one. A trigger can't
/// stand in for that other save: SQLite rolls a trigger's insert back along with the insert that fails, so
/// the row would be gone when the service looks after the refusal.
/// </summary>
internal sealed class BeforeEachSave(TempRewind rewind, Func<CancellationToken, Task> before) : IDbContextFactory<RewindDb>
{
    public RewindDb CreateDbContext() =>
        new(new DbContextOptionsBuilder<RewindDb>()
            .UseSqlite(RewindDb.ConnectionStringFor(rewind.Paths.Database))
            .AddInterceptors(new Interceptor(before))
            .Options);

    private sealed class Interceptor(Func<CancellationToken, Task> before) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await before(cancellationToken);
            return result;
        }
    }
}
